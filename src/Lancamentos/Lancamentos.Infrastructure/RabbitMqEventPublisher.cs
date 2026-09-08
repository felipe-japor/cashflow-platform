using System.Diagnostics;
using System.Text;
using Lancamentos.Application.Ports;
using Microsoft.Extensions.Options;
using OpenTelemetry;
using OpenTelemetry.Context.Propagation;
using RabbitMQ.Client;

namespace Lancamentos.Infrastructure;

/// <summary>
/// Implementação de <see cref="IEventPublisher"/> via RabbitMQ.Client — broker local de
/// referência (ADR-004/ADR-005; Azure Service Bus na arquitetura-alvo fica para quando a troca
/// for de fato necessária, resolvida por configuração/DI, não por reescrita — ADR-005).
/// </summary>
/// <remarks>
/// A conexão é aberta sob demanda, na primeira publicação, não no construtor: o host precisa
/// subir mesmo que o broker ainda não esteja disponível (ex.: ordem de start do docker-compose),
/// e o DI precisa resolver esta classe sem I/O. O worker que efetivamente drena a outbox
/// chamando esta porta é a issue #10; aqui só a porta e a implementação de infraestrutura.
/// </remarks>
public sealed class RabbitMqEventPublisher(IOptions<RabbitMqOptions> options)
    : IEventPublisher, IAsyncDisposable
{
    private readonly RabbitMqOptions _options = options.Value;
    private readonly SemaphoreSlim _connectLock = new(1, 1);
    private IConnection? _connection;
    private IChannel? _channel;

    public async Task PublishAsync(string eventType, string payload, CancellationToken cancellationToken)
    {
        var channel = await GetChannelAsync(cancellationToken);
        var body = Encoding.UTF8.GetBytes(payload);

        // Span Producer (issue #21): não existe auto-instrumentação para RabbitMQ.Client, então
        // o span e a propagação do trace context são manuais. O nome segue a convenção de
        // semântica de mensageria da OpenTelemetry ("<destino> <operação>").
        using var activity = Telemetry.ActivitySource.StartActivity($"{_options.Exchange} publish", ActivityKind.Producer);
        activity?.SetTag("messaging.system", "rabbitmq");
        activity?.SetTag("messaging.destination.name", _options.Exchange);
        activity?.SetTag("messaging.operation.name", "publish");

        var properties = new BasicProperties
        {
            // Timestamp da mensagem = momento da publicação (não o de ocorrência do evento de
            // domínio) — é o dado que o Consolidado usa para a métrica de lag de consolidação
            // (docs/observability.md). O lag entre ocorrência e publicação já é coberto,
            // separadamente, por Telemetry.OutboxLagMs no worker de outbox.
            Timestamp = new AmqpTimestamp(DateTimeOffset.UtcNow.ToUnixTimeSeconds()),
            Headers = new Dictionary<string, object?>()
        };
        InjectTraceContext(activity, properties.Headers!);

        await channel.BasicPublishAsync(
            exchange: _options.Exchange,
            routingKey: eventType,
            mandatory: false,
            basicProperties: properties,
            body: body,
            cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Injeta o trace context (W3C traceparent) do <paramref name="activity"/> corrente nos
    /// headers AMQP. Público (mesmo convenção de <see cref="OutboxPublisherWorker.PublicarPendentesAsync"/>)
    /// para permitir que testes de unidade exercitem a injeção isoladamente, sem canal/conexão
    /// real.
    /// </summary>
    public static void InjectTraceContext(Activity? activity, IDictionary<string, object?> headers)
    {
        if (activity is null)
        {
            return;
        }

        Propagators.DefaultTextMapPropagator.Inject(
            new PropagationContext(activity.Context, Baggage.Current),
            headers,
            static (carrier, key, value) => carrier[key] = value);
    }

    private async Task<IChannel> GetChannelAsync(CancellationToken cancellationToken)
    {
        if (_channel is not null)
        {
            return _channel;
        }

        await _connectLock.WaitAsync(cancellationToken);
        try
        {
            if (_channel is not null)
            {
                return _channel;
            }

            var factory = new ConnectionFactory
            {
                HostName = _options.HostName,
                Port = _options.Port,
                UserName = _options.UserName,
                Password = _options.Password
            };

            _connection = await factory.CreateConnectionAsync(cancellationToken);
            _channel = await _connection.CreateChannelAsync(cancellationToken: cancellationToken);
            await _channel.ExchangeDeclareAsync(
                _options.Exchange,
                ExchangeType.Topic,
                durable: true,
                autoDelete: false,
                cancellationToken: cancellationToken);

            return _channel;
        }
        finally
        {
            _connectLock.Release();
        }
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

        _connectLock.Dispose();
    }
}
