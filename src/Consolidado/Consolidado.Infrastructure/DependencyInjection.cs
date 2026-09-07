using Consolidado.Application.Ports;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Consolidado.Infrastructure;

/// <summary>
/// Composition root da camada de infraestrutura do serviço Consolidado. O registro condicional
/// por ambiente aqui (hoje só RabbitMQ local) já cumpre o papel de uma Factory na borda de
/// mensageria — sem precisar de uma Factory explícita (decisão de estrutura da issue #6,
/// ADR-005).
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AddConsolidadoInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<ConsolidadoDbContext>(options =>
            options.UseNpgsql(configuration.GetConnectionString("ConsolidadoDb")));

        services.Configure<RabbitMqOptions>(configuration.GetSection(RabbitMqOptions.SectionName));

        services.AddScoped<IDailyConsolidationRepository, DailyConsolidationRepository>();
        services.AddSingleton<IEventConsumer, RabbitMqEventConsumer>();

        return services;
    }
}
