using System.Diagnostics;
using OpenTelemetry.Context.Propagation;

namespace Lancamentos.Infrastructure.Tests;

/// <summary>
/// Prova a injeção do trace context nos headers AMQP (issue #21) — a parte isolável e testável
/// sem canal/conexão real de <see cref="RabbitMqEventPublisher"/>. A correlação end-to-end
/// publish→consume (headers efetivamente atravessando o broker) é coberta pelo teste E2E
/// (tests/EndToEnd.Tests).
/// </summary>
public class RabbitMqEventPublisherTests
{
    // Em produção, quem configura o propagador W3C global é a própria SDK OpenTelemetry ao
    // montar o TracerProvider (AddObservability, via .WithTracing(...)). Este teste roda
    // isolado, sem nenhum TracerProvider — sem esta linha, Propagators.DefaultTextMapPropagator
    // fica no default "no-op" da API e a injeção não escreveria nada, mascarando o próprio
    // comportamento sob teste.
    static RabbitMqEventPublisherTests() => OpenTelemetry.Sdk.SetDefaultTextMapPropagator(new TraceContextPropagator());

    [Fact]
    public void Injeta_o_traceparent_nos_headers_quando_ha_uma_activity_corrente()
    {
        using var activity = new Activity("teste-publish");
        activity.Start();
        var headers = new Dictionary<string, object?>();

        RabbitMqEventPublisher.InjectTraceContext(activity, headers);

        Assert.True(headers.ContainsKey("traceparent"));
        var traceparent = Assert.IsType<string>(headers["traceparent"]);
        Assert.Contains(activity.TraceId.ToString(), traceparent);
        Assert.Contains(activity.SpanId.ToString(), traceparent);
    }

    [Fact]
    public void Nao_escreve_headers_quando_nao_ha_activity_corrente()
    {
        var headers = new Dictionary<string, object?>();

        RabbitMqEventPublisher.InjectTraceContext(activity: null, headers);

        Assert.Empty(headers);
    }
}
