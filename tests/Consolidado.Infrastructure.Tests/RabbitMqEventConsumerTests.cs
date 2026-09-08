using System.Diagnostics.Metrics;
using System.Text;
using OpenTelemetry.Context.Propagation;
using RabbitMQ.Client;

namespace Consolidado.Infrastructure.Tests;

/// <summary>
/// Prova a extração do trace context dos headers AMQP (issue #21) — a parte isolável e testável
/// sem canal/conexão real de <see cref="RabbitMqEventConsumer"/>. A correlação end-to-end
/// publish→consume (headers efetivamente atravessando o broker) é coberta pelo teste E2E
/// (tests/EndToEnd.Tests).
/// </summary>
public class RabbitMqEventConsumerTests
{
    // Em produção, quem configura o propagador W3C global é a própria SDK OpenTelemetry ao
    // montar o TracerProvider (AddObservability, via .WithTracing(...)). Este teste roda
    // isolado, sem nenhum TracerProvider — sem esta linha, Propagators.DefaultTextMapPropagator
    // fica no default "no-op" da API e a extração não leria nada, mascarando o próprio
    // comportamento sob teste.
    static RabbitMqEventConsumerTests() => OpenTelemetry.Sdk.SetDefaultTextMapPropagator(new TraceContextPropagator());

    [Fact]
    public void Extrai_o_trace_context_de_um_header_traceparent_valido()
    {
        // RabbitMQ.Client entrega os valores dos headers como byte[] na recepção, mesmo quando
        // o publisher escreveu string — reproduz esse formato real.
        const string traceId = "4bf92f3577b34da6a3ce929d0e0e4736";
        const string spanId = "00f067aa0ba902b7";
        var headers = new Dictionary<string, object?>
        {
            ["traceparent"] = Encoding.UTF8.GetBytes($"00-{traceId}-{spanId}-01")
        };

        var context = RabbitMqEventConsumer.ExtractTraceContext(headers);

        Assert.Equal(traceId, context.TraceId.ToString());
        Assert.Equal(spanId, context.SpanId.ToString());
    }

    [Fact]
    public void Retorna_contexto_vazio_quando_nao_ha_header_de_trace()
    {
        var context = RabbitMqEventConsumer.ExtractTraceContext(new Dictionary<string, object?>());

        Assert.Equal(default, context.TraceId);
        Assert.Equal(default, context.SpanId);
    }

    [Fact]
    public void Retorna_contexto_vazio_quando_headers_e_nulo()
    {
        var context = RabbitMqEventConsumer.ExtractTraceContext(null);

        Assert.Equal(default, context.TraceId);
        Assert.Equal(default, context.SpanId);
    }

    [Fact]
    public void Registra_o_lag_de_consolidacao_quando_o_timestamp_amqp_e_valido()
    {
        var publicadoEmUtc = DateTimeOffset.UtcNow.AddMilliseconds(-500);
        var timestamp = new AmqpTimestamp(publicadoEmUtc.ToUnixTimeSeconds());

        var medicoes = CapturarMedicoesDoHistograma(
            () => RabbitMqEventConsumer.RegistrarLagDeConsolidacao(timestamp));

        var valor = Assert.Single(medicoes);
        // Timestamp AMQP tem resolução de segundo (issue #21, docs/observability.md) — a
        // tolerância cobre o arredondamento sem mascarar um cálculo errado (ex.: sinal invertido).
        Assert.InRange(valor, 0, 2_000);
    }

    [Fact]
    public void Nao_registra_lag_de_consolidacao_quando_o_timestamp_amqp_e_zero_ou_negativo()
    {
        var medicoes = CapturarMedicoesDoHistograma(
            () => RabbitMqEventConsumer.RegistrarLagDeConsolidacao(new AmqpTimestamp(0)));

        Assert.Empty(medicoes);
    }

    /// <summary>
    /// Captura, via <see cref="MeterListener"/>, os valores efetivamente gravados no histograma
    /// <see cref="Telemetry.ConsolidacaoLagMs"/> durante a execução de <paramref name="acao"/> —
    /// prova que a métrica de negócio (issue #21) é de fato emitida (e com que valor), não só que
    /// o código roda sem lançar exceção.
    /// </summary>
    private static List<double> CapturarMedicoesDoHistograma(Action acao)
    {
        var medicoes = new List<double>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == Telemetry.ServiceName)
            {
                l.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<double>((_, valor, _, _) => medicoes.Add(valor));
        listener.Start();

        acao();

        return medicoes;
    }
}
