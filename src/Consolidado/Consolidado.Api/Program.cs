using Consolidado.Api;
using Consolidado.Application.UseCases;
using Consolidado.Infrastructure;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();
builder.Services.AddConsolidadoInfrastructure(builder.Configuration);
builder.Services.AddScoped<ConsultarConsolidadoDiaHandler>();
builder.Services.AddScoped<ConsultarConsolidadoPeriodoHandler>();

// Readiness verifica só o PostgreSQL — único dado real que os endpoints de consulta leem no
// caminho HTTP síncrono; o broker fica de fora por decisão explícita (ADR-007, issue #15).
builder.Services.AddHealthChecks()
    .AddNpgSql(
        builder.Configuration.GetConnectionString("ConsolidadoDb")!,
        name: "postgres",
        tags: ["ready"]);

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

// Aplica migrations pendentes no startup, controlado por flag de configuração (default: off) —
// mesmo racional do serviço Lançamentos (issue #8): WebApplicationFactory sobe o host sem
// Postgres disponível, e a flag só é ligada no docker-compose, onde o Postgres já está de pé
// (depends_on: condition: service_healthy) antes do serviço subir.
if (builder.Configuration.GetValue<bool>("Database:MigrateOnStartup"))
{
    using var scope = app.Services.CreateScope();
    var dbContext = scope.ServiceProvider.GetRequiredService<ConsolidadoDbContext>();
    dbContext.Database.Migrate();
}

// Endpoints de consulta do saldo consolidado (RF04, issue #14).
//
// Dia sem nenhum lançamento processado ainda: escolhido 404 (não um objeto zerado) — o recurso
// "posição do dia X" simplesmente não existe no read model ainda. Consistente com o período:
// dias sem registro dentro do intervalo não aparecem na lista (nem 404 nem zero-preenchidos),
// mesma decisão de fundo em formato adequado a cada shape (recurso singular vs. coleção) —
// "ausência de dado" nunca vira erro nem valor sintético, mesmo espírito de RF02.
app.MapGet("/consolidado/{data}", async (
    DateOnly data,
    ConsultarConsolidadoDiaHandler handler,
    CancellationToken cancellationToken) =>
{
    var command = new ConsultarConsolidadoDiaCommand(data);
    var consolidado = await handler.Handle(command, cancellationToken);

    return consolidado is null ? Results.NotFound() : Results.Ok(ConsolidadoResponse.De(consolidado));
});

app.MapGet("/consolidado", async (
    DateOnly dataInicial,
    DateOnly dataFinal,
    ConsultarConsolidadoPeriodoHandler handler,
    CancellationToken cancellationToken) =>
{
    var command = new ConsultarConsolidadoPeriodoCommand(dataInicial, dataFinal);
    var consolidados = await handler.Handle(command, cancellationToken);

    return Results.Ok(consolidados.Select(ConsolidadoResponse.De));
});

app.MapGet("/", () => Results.Ok(new { service = "Consolidado" }));

// Liveness: só confirma que o processo está de pé, sem tocar nenhuma dependência externa
// (Predicate ==> nenhuma checagem registrada é executada).
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });

// Readiness: só a checagem tagueada "ready" (PostgreSQL) — ver ADR-007 para o racional de
// deixar o broker fora.
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") });

app.Run();

// Necessário como partial class pública para o WebApplicationFactory<Program> dos testes de
// integração (Consolidado.Application.Tests) enxergar este Program de fora do assembly.
public partial class Program;
