using Lancamentos.Application.Ports;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Lancamentos.Infrastructure;

/// <summary>
/// Composition root da camada de infraestrutura do serviço Lançamentos. O registro condicional
/// por ambiente aqui (hoje só RabbitMQ local) já cumpre o papel de uma Factory na borda de
/// mensageria — sem precisar de uma Factory explícita (decisão de estrutura da issue #6,
/// ADR-005).
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AddLancamentosInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<LancamentosDbContext>(options =>
            options.UseNpgsql(configuration.GetConnectionString("LancamentosDb")));

        services.Configure<RabbitMqOptions>(configuration.GetSection(RabbitMqOptions.SectionName));

        services.AddScoped<ITransactionRepository, TransactionRepository>();
        services.AddSingleton<IEventPublisher, RabbitMqEventPublisher>();

        return services;
    }
}
