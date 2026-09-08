using Consolidado.Application.Ports;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

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
        services.Configure<EventConsumerWorkerOptions>(configuration.GetSection(EventConsumerWorkerOptions.SectionName));

        services.AddScoped<IDailyConsolidationRepository, DailyConsolidationRepository>();
        services.AddSingleton<IEventConsumer, RabbitMqEventConsumer>();
        services.AddHostedService<EventConsumerWorker>();

        services.AddObservability();

        return services;
    }

    /// <summary>
    /// Observabilidade (issue #21, ver docs/observability.md): traces, métricas e logs via
    /// OpenTelemetry, export único via OTLP (vendor-neutral, decisão já registrada em CLAUDE.md).
    /// O endpoint OTLP é lido pelo próprio exportador da variável de ambiente padrão
    /// <c>OTEL_EXPORTER_OTLP_ENDPOINT</c> (comportamento default do SDK, sem código extra aqui) —
    /// mesmo padrão de configuração via ambiente já usado para <c>RabbitMq__HostName</c> etc.
    /// </summary>
    private static IServiceCollection AddObservability(this IServiceCollection services)
    {
        services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService(Telemetry.ServiceName))
            .WithTracing(tracing => tracing
                .AddAspNetCoreInstrumentation()
                .AddNpgsql()
                .AddSource(Telemetry.ServiceName) // consume manual do RabbitMQ (issue #21)
                .AddOtlpExporter())
            .WithMetrics(metrics => metrics
                .AddAspNetCoreInstrumentation()
                .AddRuntimeInstrumentation()
                .AddNpgsqlInstrumentation()
                .AddMeter(Telemetry.ServiceName) // lag de consolidação (issue #21)
                .AddOtlpExporter())
            .WithLogging(logging => logging.AddOtlpExporter());

        return services;
    }
}
