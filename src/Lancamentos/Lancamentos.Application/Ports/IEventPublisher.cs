namespace Lancamentos.Application.Ports;

/// <summary>
/// Porta de publicação de eventos na borda de mensageria. O código de aplicação/domínio depende
/// só desta interface, nunca do SDK concreto do broker (RabbitMQ localmente, Azure Service Bus
/// na arquitetura-alvo — ADR-004, ADR-005). Qual implementação é usada é resolvido por registro
/// condicional no DI container, que cumpre o papel de Factory sem precisar de uma Factory
/// explícita (decisão de estrutura da issue #6).
/// </summary>
/// <remarks>
/// Quem chama esta porta para de fato drenar a outbox (poll + publish, com retry) é o worker da
/// issue #10 — aqui só existe a porta e a implementação de infraestrutura que a satisfaz.
/// </remarks>
public interface IEventPublisher
{
    /// <param name="eventType">Nome do evento de domínio (ex.: "LancamentoRegistrado").</param>
    /// <param name="payload">Payload serializado (JSON) do evento.</param>
    Task PublishAsync(string eventType, string payload, CancellationToken cancellationToken);
}
