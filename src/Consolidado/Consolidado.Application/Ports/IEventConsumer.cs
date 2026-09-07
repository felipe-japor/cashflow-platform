namespace Consolidado.Application.Ports;

/// <summary>
/// Porta de consumo de eventos na borda de mensageria — espelha <c>IEventPublisher</c> do lado
/// Lançamentos. O código de aplicação depende só desta interface, nunca do SDK concreto do
/// broker (RabbitMQ localmente, Azure Service Bus na arquitetura-alvo — ADR-004, ADR-005).
/// </summary>
/// <remarks>
/// Orquestrar o consumo de fato — desserializar o payload, aplicar o delta via
/// <see cref="IDailyConsolidationRepository"/>, tratar DLQ para falhas persistentes — é escopo
/// das issues #11 e #12. Aqui só existe a porta e a implementação de infraestrutura que a
/// satisfaz, provando que o DI resolve.
/// </remarks>
public interface IEventConsumer
{
    /// <param name="onMessage">
    /// Callback invocado para cada mensagem recebida (tipo do evento, payload serializado).
    /// A mensagem só é confirmada (ack) ao broker se o callback concluir sem lançar exceção.
    /// </param>
    /// <param name="cancellationToken">
    /// Token de lifecycle do host (<c>IHostApplicationLifetime.ApplicationStopping</c>) — este
    /// processo roda em background, não amarrado a nenhuma requisição.
    /// </param>
    Task StartConsumingAsync(Func<string, string, CancellationToken, Task> onMessage, CancellationToken cancellationToken);
}
