using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.DependencyInjection;

namespace Consolidado.Application.Tests;

/// <summary>
/// Health check falso, com resultado controlado pelo teste — substitui a checagem real do
/// PostgreSQL (<c>AddNpgSql</c>, registrada no Program.cs) para provar a fiação dos endpoints
/// (issue #15/ADR-007) sem depender de um Postgres real disponível/indisponível no ambiente de
/// execução dos testes.
/// </summary>
public class FakeHealthCheck(HealthStatus status) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
        => Task.FromResult(new HealthCheckResult(status));
}

/// <summary>
/// Factory de testes que troca a checagem real do PostgreSQL por <see cref="FakeHealthCheck"/>,
/// mantendo a mesma tag "ready" usada em produção — prova que <c>/health/ready</c> de fato
/// consulta essa tag e que <c>/health/live</c> não consulta nenhuma (ADR-007).
/// </summary>
public class HealthCheckTestFactory(HealthStatus statusDaChecagemReady) : WebApplicationFactory<global::Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            services.Configure<HealthCheckServiceOptions>(options =>
            {
                options.Registrations.Clear();
                options.Registrations.Add(new HealthCheckRegistration(
                    "postgres",
                    _ => new FakeHealthCheck(statusDaChecagemReady),
                    failureStatus: null,
                    tags: ["ready"]));
            });
        });
    }
}
