using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Consolidado.Infrastructure;

/// <summary>
/// Instrumentação manual deste serviço (issue #21) — não coberta por nenhum pacote de
/// auto-instrumentação: o span de consumo do RabbitMQ e a métrica de negócio de lag de
/// consolidação, ambos usados por <see cref="RabbitMqEventConsumer"/>. <see cref="ActivitySource"/>
/// e o <see cref="Meter"/> por trás do histograma são registrados no pipeline OpenTelemetry via
/// <c>AddSource</c>/<c>AddMeter</c> em
/// <see cref="DependencyInjection.AddConsolidadoInfrastructure"/> — sem essa chamada, os sinais
/// são criados mas nunca exportados (comportamento padrão do SDK).
/// </summary>
public static class Telemetry
{
    public const string ServiceName = "Consolidado";

    public static readonly ActivitySource ActivitySource = new(ServiceName);

    private static readonly Meter Meter = new(ServiceName);

    /// <summary>
    /// Tempo entre o momento em que a mensagem foi publicada no broker (<c>BasicProperties.Timestamp</c>,
    /// atribuído pelo serviço Lançamentos ao publicar — ver docs/observability.md) e o momento em
    /// que o consolidado é efetivamente atualizado (só registrada quando o consumo termina com
    /// sucesso — falha não atualiza nada, não há o que medir). Complementa o histograma de lag do
    /// outbox do lado Lançamentos: juntas, as duas métricas cobrem o intervalo completo de
    /// consistência eventual descrito no ADR-001/ADR-002 (lançamento ocorrido → evento publicado
    /// → consolidado atualizado).
    /// </summary>
    public static readonly Histogram<double> ConsolidacaoLagMs = Meter.CreateHistogram<double>(
        "consolidado.consolidacao.lag_ms",
        unit: "ms",
        description: "Tempo entre a publicação do evento no broker e o consolidado diário ser atualizado.");
}
