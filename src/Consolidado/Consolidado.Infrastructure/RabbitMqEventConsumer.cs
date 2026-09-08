using System.Diagnostics;
using System.Text;
using Consolidado.Application.Ports;
using Microsoft.Extensions.Options;
using OpenTelemetry.Context.Propagation;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace Consolidado.Infrastructure;

/// <summary>
/// Implementação de <see cref="IEventConsumer"/> via RabbitMQ.Client — broker local de
/// referência (ADR-004/ADR-005; Azure Service Bus na arquitetura-alvo fica para quando a troca
/// for de fato necessária, resolvida por configuração/DI, não por reescrita — ADR-005).
/// </summary>
/// <remarks>
/// A conexão/canal são abertos só quando <see cref="StartConsumingAsync"/> é chamado (issue #12),
/// não no construtor — o DI resolve esta classe sem I/O, o host sobe mesmo que o broker ainda
/// não esteja disponível. A mensagem só é confirmada (ack) se o callback concluir sem lançar; se
/// o callback lançar (esgotado o retry curto orquestrado por quem chama — issue #11), a mensagem
/// é nack'ada sem requeue. A fila principal é declarada com <c>x-dead-letter-exchange</c> apontando
/// para a dead-letter exchange/queue: o próprio RabbitMQ roteia a mensagem nack'ada para lá
/// automaticamente, sem nenhuma lógica própria de redelivery ou contagem aqui.
/// </remarks>
public sealed class RabbitMqEventConsumer(IOptions<RabbitMqOptions> options)
    : IEventConsumer, IAsyncDisposable
{
    private readonly RabbitMqOptions _options = options.Value;
    private IConnection? _connection;
    private IChannel? _channel;

    public async Task StartConsumingAsync(Func<string, string, CancellationToken, Task> onMessage, CancellationToken cancellationToken)
    {
        var factory = new ConnectionFactory
        {
            HostName = _options.HostName,
            Port = _options.Port,
            UserName = _options.UserName,
            Password = _options.Password
        };

        _connection = await factory.CreateConnectionAsync(cancellationToken);
        _channel = await _connection.CreateChannelAsync(cancellationToken: cancellationToken);

        await _channel.ExchangeDeclareAsync(_options.Exchange, ExchangeType.Topic, durable: true, autoDelete: false, cancellationToken: cancellationToken);

        // Dead-letter (issue #11): declarada antes da fila principal, que referencia esta
        // exchange via x-dead-letter-exchange. Fanout porque o único propósito da DLQ é capturar
        // tudo que for nack'ado sem requeue — não há necessidade de roteamento por routing key.
        await _channel.ExchangeDeclareAsync(_options.DeadLetterExchange, ExchangeType.Fanout, durable: true, autoDelete: false, cancellationToken: cancellationToken);
        await _channel.QueueDeclareAsync(_options.DeadLetterQueue, durable: true, exclusive: false, autoDelete: false, cancellationToken: cancellationToken);
        await _channel.QueueBindAsync(_options.DeadLetterQueue, _options.DeadLetterExchange, routingKey: string.Empty, cancellationToken: cancellationToken);

        var argumentosFilaPrincipal = new Dictionary<string, object?> { ["x-dead-letter-exchange"] = _options.DeadLetterExchange };
        await _channel.QueueDeclareAsync(_options.Queue, durable: true, exclusive: false, autoDelete: false, arguments: argumentosFilaPrincipal, cancellationToken: cancellationToken);
        await _channel.QueueBindAsync(_options.Queue, _options.Exchange, _options.RoutingKey, cancellationToken: cancellationToken);

        var consumer = new AsyncEventingBasicConsumer(_channel);
        consumer.ReceivedAsync += async (_, delivery) =>
        {
            var payload = Encoding.UTF8.GetString(delivery.Body.Span);
            var eventType = delivery.BasicProperties.Type ?? _options.RoutingKey;

            // Span Consumer (issue #21): não existe auto-instrumentação para RabbitMQ.Client, o
            // trace context é extraído manualmente dos headers AMQP injetados pelo Lançamentos ao
            // publicar — correlaciona publish↔consume entre os dois serviços. Reconectar até o
            // request HTTP original que originou o lançamento é deliberadamente fora de escopo
            // (docs/observability.md).
            var parentContext = ExtractTraceContext(delivery.BasicProperties.Headers);
            using var activity = Telemetry.ActivitySource.StartActivity($"{_options.Queue} process", ActivityKind.Consumer, parentContext);
            activity?.SetTag("messaging.system", "rabbitmq");
            activity?.SetTag("messaging.destination.name", _options.Queue);
            activity?.SetTag("messaging.operation.name", "process");

            try
            {
                await onMessage(eventType, payload, cancellationToken);
                await _channel.BasicAckAsync(delivery.DeliveryTag, multiple: false, cancellationToken);

                RegistrarLagDeConsolidacao(delivery.BasicProperties.Timestamp);
            }
            catch (Exception)
            {
                // Callback já esgotou o retry curto (issue #11) — nack sem requeue. O RabbitMQ
                // roteia automaticamente para a dead-letter exchange configurada na fila.
                await _channel.BasicNackAsync(delivery.DeliveryTag, multiple: false, requeue: false, cancellationToken);
            }
        };

        await _channel.BasicConsumeAsync(_options.Queue, autoAck: false, consumer, cancellationToken);
    }

    /// <summary>
    /// Métrica de negócio (issue #21): tempo entre a mensagem ter sido publicada (timestamp AMQP
    /// atribuído pelo serviço Lançamentos ao publicar) e este consumo ter
    /// concluído com sucesso (consolidado já atualizado). Complementa a métrica de lag do outbox
    /// do lado Lançamentos — juntas cobrem o intervalo completo de consistência eventual do
    /// ADR-001/ADR-002. Mensagens sem timestamp (<c>UnixTime == 0</c>) não são contabilizadas —
    /// não deveria acontecer em produção (o publisher sempre atribui), mas evita poluir o
    /// histograma com um valor absurdo (~56 anos) se algum dia acontecer.
    /// </summary>
    private static void RegistrarLagDeConsolidacao(AmqpTimestamp publicadoEm)
    {
        if (publicadoEm.UnixTime <= 0)
        {
            return;
        }

        var publicadoEmUtc = DateTimeOffset.FromUnixTimeSeconds(publicadoEm.UnixTime).UtcDateTime;
        Telemetry.ConsolidacaoLagMs.Record((DateTime.UtcNow - publicadoEmUtc).TotalMilliseconds);
    }

    /// <summary>
    /// Extrai o trace context (W3C traceparent) dos headers AMQP. Público (mesma convenção de
    /// <see cref="EventConsumerWorker.ProcessarAsync"/>) para permitir que testes de unidade
    /// exercitem a extração isoladamente, sem canal/conexão real ou <c>BasicDeliverEventArgs</c>.
    /// </summary>
    public static ActivityContext ExtractTraceContext(IDictionary<string, object?>? headers) =>
        Propagators.DefaultTextMapPropagator.Extract(default, headers, ExtrairValorDoHeader).ActivityContext;

    private static IEnumerable<string> ExtrairValorDoHeader(IDictionary<string, object?>? headers, string key)
    {
        if (headers is null || !headers.TryGetValue(key, out var valor) || valor is null)
        {
            return [];
        }

        return valor switch
        {
            byte[] bytes => [Encoding.UTF8.GetString(bytes)],
            string texto => [texto],
            _ => []
        };
    }

    public async ValueTask DisposeAsync()
    {
        if (_channel is not null)
        {
            await _channel.DisposeAsync();
        }

        if (_connection is not null)
        {
            await _connection.DisposeAsync();
        }
    }
}
