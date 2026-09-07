using Consolidado.Application.Ports;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Consolidado.Application.Tests;

/// <summary>
/// Prova que o host do serviço Consolidado sobe (composition root do Program.cs) e que o
/// container de DI resolve as portas de aplicação (IDailyConsolidationRepository, IEventConsumer)
/// para as implementações concretas de Infrastructure — sem depender de Postgres/RabbitMQ
/// estarem de pé, pois nenhuma das duas implementações abre conexão de I/O na construção
/// (decisão de estrutura da issue #6).
/// </summary>
public class ConsolidadoHostTests : IClassFixture<WebApplicationFactory<global::Program>>
{
    private readonly WebApplicationFactory<global::Program> _factory;

    public ConsolidadoHostTests(WebApplicationFactory<global::Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Host_sobe_e_responde_na_raiz()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/");

        response.EnsureSuccessStatusCode();
    }

    [Fact]
    public void DI_resolve_IDailyConsolidationRepository_para_a_implementacao_de_infraestrutura()
    {
        using var scope = _factory.Services.CreateScope();

        var repository = scope.ServiceProvider.GetRequiredService<IDailyConsolidationRepository>();

        Assert.IsType<Consolidado.Infrastructure.DailyConsolidationRepository>(repository);
    }

    [Fact]
    public void DI_resolve_IEventConsumer_para_a_implementacao_de_infraestrutura()
    {
        var consumer = _factory.Services.GetRequiredService<IEventConsumer>();

        Assert.IsType<Consolidado.Infrastructure.RabbitMqEventConsumer>(consumer);
    }
}
