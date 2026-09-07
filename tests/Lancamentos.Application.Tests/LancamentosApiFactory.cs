using Lancamentos.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Lancamentos.Application.Tests;

/// <summary>
/// Factory de testes que substitui o <see cref="LancamentosDbContext"/> configurado para
/// PostgreSQL (produção/docker-compose) por EF Core InMemory — os testes de integração do
/// endpoint (<c>RegistrarLancamentoEndpointTests</c>) não devem depender de um Postgres real
/// disponível no ambiente de execução dos testes/CI.
/// </summary>
public class LancamentosApiFactory : WebApplicationFactory<global::Program>
{
    // Um nome fixo por instância da factory — xUnit cria uma instância nova de
    // RegistrarLancamentoEndpointTests (e, portanto, desta factory) por teste, garantindo banco
    // isolado por teste sem depender de reset manual de estado entre eles.
    private readonly string _databaseName = $"lancamentos-tests-{Guid.NewGuid()}";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            // AddDbContext registra mais do que o descriptor de DbContextOptions<T> em si (ex.:
            // IDbContextOptionsConfiguration<T>, usado para combinar configurações de múltiplas
            // chamadas a AddDbContext) — remover só DbContextOptions<T> deixa a configuração do
            // Npgsql "grudada" e faz o EF Core reclamar de dois provedores registrados ao mesmo
            // tempo. Removendo por reflexão qualquer serviço que referencie LancamentosDbContext
            // como argumento genérico (ou o próprio tipo) garante que nada da configuração
            // original (Npgsql) sobrevive antes de registrar a versão InMemory.
            var descritoresDoDbContext = services
                .Where(d => d.ServiceType == typeof(LancamentosDbContext)
                    || (d.ServiceType.IsGenericType && d.ServiceType.GetGenericArguments().Contains(typeof(LancamentosDbContext))))
                .ToList();

            foreach (var descritor in descritoresDoDbContext)
            {
                services.Remove(descritor);
            }

            services.AddDbContext<LancamentosDbContext>(options =>
                options.UseInMemoryDatabase(_databaseName));
        });
    }
}
