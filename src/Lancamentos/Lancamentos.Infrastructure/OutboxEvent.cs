namespace Lancamentos.Infrastructure;

/// <summary>
/// Registro de outbox (ADR-002): linha persistida na mesma transação de banco que o
/// <see cref="Lancamentos.Domain.Transaction"/> que a originou, garantindo atomicidade entre a
/// escrita do lançamento e a publicação do evento — sem depender do broker estar disponível no
/// momento da escrita.
/// </summary>
/// <remarks>
/// Tipo de persistência, não um conceito de domínio ou de aplicação — por isso vive na camada de
/// Infrastructure, não é exposto pela porta <c>ITransactionRepository</c>. Schema definitivo e a
/// gravação atômica completa são escopo da issue #9.
/// </remarks>
public class OutboxEvent
{
    public Guid Id { get; private set; }
    public string EventType { get; private set; }
    public string Payload { get; private set; }
    public DateTime OcorridoEmUtc { get; private set; }
    public DateTime? PublicadoEmUtc { get; private set; }

    private OutboxEvent()
    {
        EventType = string.Empty;
        Payload = string.Empty;
    }

    public OutboxEvent(string eventType, string payload)
    {
        Id = Guid.NewGuid();
        EventType = eventType;
        Payload = payload;
        OcorridoEmUtc = DateTime.UtcNow;
    }
}
