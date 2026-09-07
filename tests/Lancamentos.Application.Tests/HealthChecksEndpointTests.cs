using System.Net;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Lancamentos.Application.Tests;

/// <summary>
/// Prova o escopo do readiness definido no ADR-007: <c>/health/live</c> nunca consulta
/// dependências externas, <c>/health/ready</c> reflete só a checagem tagueada "ready"
/// (PostgreSQL, sem o broker).
/// </summary>
public class HealthChecksEndpointTests
{
    [Fact]
    public async Task Live_retorna_saudavel_mesmo_com_a_dependencia_ready_indisponivel()
    {
        using var factory = new HealthCheckTestFactory(HealthStatus.Unhealthy);
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health/live");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Ready_retorna_503_quando_a_checagem_taggeada_ready_esta_indisponivel()
    {
        using var factory = new HealthCheckTestFactory(HealthStatus.Unhealthy);
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health/ready");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    [Fact]
    public async Task Ready_retorna_200_quando_a_checagem_taggeada_ready_esta_saudavel()
    {
        using var factory = new HealthCheckTestFactory(HealthStatus.Healthy);
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health/ready");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
