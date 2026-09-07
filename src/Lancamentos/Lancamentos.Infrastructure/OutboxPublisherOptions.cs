namespace Lancamentos.Infrastructure;

/// <summary>
/// Configuração do worker que drena a outbox para o broker (issue #10). Vinculada à seção
/// "OutboxPublisher" de configuração (appsettings/variáveis de ambiente).
/// </summary>
public class OutboxPublisherOptions
{
    public const string SectionName = "OutboxPublisher";

    /// <summary>Intervalo entre execuções do poll. O próprio ciclo de polling é o mecanismo de
    /// retry para falha transitória (issue #10) — sem backoff exponencial, sem fila separada.</summary>
    public int IntervaloSegundos { get; set; } = 5;

    /// <summary>Quantidade máxima de eventos pendentes processados por iteração do poll.</summary>
    public int TamanhoLote { get; set; } = 20;

    /// <summary>Tentativas de publicação malsucedidas até o evento ser marcado como falha
    /// persistente (<see cref="OutboxEvent.FalhaPersistenteEmUtc"/>) e parar de ser tentado
    /// (issue #11).</summary>
    public int LimiteTentativas { get; set; } = 5;
}
