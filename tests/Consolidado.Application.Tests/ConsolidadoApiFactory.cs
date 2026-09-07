using Consolidado.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Consolidado.Application.Tests;

/// <summary>
/// Factory de testes que substitui o <see cref="ConsolidadoDbContext"/> configurado para
/// PostgreSQL (produção/docker-compose) por EF Core InMemory — mesmo padrão de
/// <c>Lancamentos.Application.Tests.LancamentosApiFactory</c> (issue #42). Os testes de integração
/// dos endpoints de consulta (issue #14) não devem depender de um Postgres real disponível no
/// ambiente de execução dos testes/CI.
/// </summary>
public class ConsolidadoApiFactory : WebApplicationFactory<global::Program>
{
    // Um nome fixo por instância da factory — xUnit cria uma instância nova de classe de teste
    // (e, portanto, desta factory) por teste, garantindo banco isolado por teste sem depender de
    // reset manual de estado entre eles.
    private readonly string _databaseName = $"consolidado-tests-{Guid.NewGuid()}";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            // Ver comentário equivalente em LancamentosApiFactory: remover só
            // DbContextOptions<T> deixa a configuração do Npgsql "grudada" e faz o EF Core
            // reclamar de dois provedores registrados ao mesmo tempo.
            var descritoresDoDbContext = services
                .Where(d => d.ServiceType == typeof(ConsolidadoDbContext)
                    || (d.ServiceType.IsGenericType && d.ServiceType.GetGenericArguments().Contains(typeof(ConsolidadoDbContext))))
                .ToList();

            foreach (var descritor in descritoresDoDbContext)
            {
                services.Remove(descritor);
            }

            services.AddDbContext<ConsolidadoDbContext>(options =>
                options.UseInMemoryDatabase(_databaseName));
        });
    }

    /// <summary>
    /// Semeia um consolidado diretamente no banco de testes — o serviço Consolidado não expõe
    /// um endpoint de escrita (a posição é construída só pelo consumo de eventos, issue #12), então
    /// os testes de consulta (issue #14) precisam popular o read model por fora do HTTP.
    /// </summary>
    public async Task SemearAsync(Domain.DailyConsolidation consolidado)
    {
        using var scope = Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ConsolidadoDbContext>();
        dbContext.DailyConsolidations.Add(consolidado);
        await dbContext.SaveChangesAsync();
    }
}
