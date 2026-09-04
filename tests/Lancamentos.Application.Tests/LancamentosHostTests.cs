using Lancamentos.Application.Ports;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Lancamentos.Application.Tests;

/// <summary>
/// Prova que o host do serviço Lançamentos sobe (composition root do Program.cs) e que o
/// container de DI resolve as portas de aplicação (ITransactionRepository, IEventPublisher)
/// para as implementações concretas de Infrastructure — sem depender de Postgres/RabbitMQ
/// estarem de pé, pois nenhuma das duas implementações abre conexão de I/O na construção
/// (decisão de estrutura da issue #6).
/// </summary>
public class LancamentosHostTests : IClassFixture<WebApplicationFactory<global::Program>>
{
    private readonly WebApplicationFactory<global::Program> _factory;

    public LancamentosHostTests(WebApplicationFactory<global::Program> factory)
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
    public void DI_resolve_ITransactionRepository_para_a_implementacao_de_infraestrutura()
    {
        using var scope = _factory.Services.CreateScope();

        var repository = scope.ServiceProvider.GetRequiredService<ITransactionRepository>();

        Assert.IsType<Lancamentos.Infrastructure.TransactionRepository>(repository);
    }

    [Fact]
    public void DI_resolve_IEventPublisher_para_a_implementacao_de_infraestrutura()
    {
        var publisher = _factory.Services.GetRequiredService<IEventPublisher>();

        Assert.IsType<Lancamentos.Infrastructure.RabbitMqEventPublisher>(publisher);
    }
}
