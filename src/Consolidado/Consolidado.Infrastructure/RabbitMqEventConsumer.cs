using System.Text;
using Consolidado.Application.Ports;
using Microsoft.Extensions.Options;
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
/// não esteja disponível. A mensagem só é confirmada (ack) se o callback concluir sem lançar;
/// tratamento de DLQ para falhas persistentes é escopo da issue #11.
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
        await _channel.QueueDeclareAsync(_options.Queue, durable: true, exclusive: false, autoDelete: false, cancellationToken: cancellationToken);
        await _channel.QueueBindAsync(_options.Queue, _options.Exchange, _options.RoutingKey, cancellationToken: cancellationToken);

        var consumer = new AsyncEventingBasicConsumer(_channel);
        consumer.ReceivedAsync += async (_, delivery) =>
        {
            var payload = Encoding.UTF8.GetString(delivery.Body.Span);
            var eventType = delivery.BasicProperties.Type ?? _options.RoutingKey;

            await onMessage(eventType, payload, cancellationToken);
            await _channel.BasicAckAsync(delivery.DeliveryTag, multiple: false, cancellationToken);
        };

        await _channel.BasicConsumeAsync(_options.Queue, autoAck: false, consumer, cancellationToken);
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
