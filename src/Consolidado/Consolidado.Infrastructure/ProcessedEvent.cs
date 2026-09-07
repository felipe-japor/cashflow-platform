namespace Consolidado.Infrastructure;

/// <summary>
/// Registro de inbox: um evento consumido e já aplicado ao <c>DailyConsolidation</c>
/// correspondente. Constraint única em <see cref="EventId"/> é o que garante a idempotência do
/// consumo (docs/domain-mapping.md) — reentrega at-least-once do broker (NFR04) é esperada, não
/// excepcional.
/// </summary>
/// <remarks>
/// Tipo de persistência, não um conceito de aplicação ou domínio — por isso vive na camada de
/// Infrastructure. Schema definitivo e a gravação atômica completa junto do delta são escopo da
/// issue #12.
/// </remarks>
public class ProcessedEvent
{
    public Guid EventId { get; private set; }
    public DateTime ProcessadoEmUtc { get; private set; }

    private ProcessedEvent()
    {
    }

    public ProcessedEvent(Guid eventId)
    {
        EventId = eventId;
        ProcessadoEmUtc = DateTime.UtcNow;
    }
}
