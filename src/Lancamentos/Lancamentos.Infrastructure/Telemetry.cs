using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Lancamentos.Infrastructure;

/// <summary>
/// Instrumentação manual deste serviço (issue #21) — não coberta por nenhum pacote de
/// auto-instrumentação: o span de publicação no RabbitMQ (usado por
/// <see cref="RabbitMqEventPublisher"/>) e a métrica de negócio de lag do outbox (usada por
/// <see cref="OutboxPublisherWorker"/>). <see cref="ActivitySource"/> e o <see cref="Meter"/>
/// por trás do histograma são registrados no pipeline OpenTelemetry via
/// <c>AddSource</c>/<c>AddMeter</c> em <see cref="DependencyInjection.AddLancamentosInfrastructure"/>
/// — sem essa chamada, os sinais são criados mas nunca exportados (comportamento padrão do SDK).
/// </summary>
public static class Telemetry
{
    public const string ServiceName = "Lancamentos";

    public static readonly ActivitySource ActivitySource = new(ServiceName);

    private static readonly Meter Meter = new(ServiceName);

    /// <summary>
    /// Tempo entre o lançamento ocorrer (<see cref="OutboxEvent.OcorridoEmUtc"/>) e o worker de
    /// outbox conseguir publicar o evento correspondente no broker — evidencia com dado real o
    /// intervalo de consistência eventual descrito no ADR-001/ADR-002, em vez de deixá-lo só
    /// como afirmação em texto.
    /// </summary>
    public static readonly Histogram<double> OutboxLagMs = Meter.CreateHistogram<double>(
        "lancamentos.outbox.lag_ms",
        unit: "ms",
        description: "Tempo entre o lançamento ocorrer e o evento correspondente ser publicado no broker.");
}
