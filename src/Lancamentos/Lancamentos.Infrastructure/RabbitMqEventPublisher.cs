using System.Text;
using Lancamentos.Application.Ports;
using Microsoft.Extensions.Options;
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

        await channel.BasicPublishAsync(
            exchange: _options.Exchange,
            routingKey: eventType,
            body: body,
            cancellationToken: cancellationToken);
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
