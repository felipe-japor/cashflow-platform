using Lancamentos.Api;
using Lancamentos.Application.UseCases;
using Lancamentos.Domain.Exceptions;
using Lancamentos.Infrastructure;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();
builder.Services.AddLancamentosInfrastructure(builder.Configuration);
builder.Services.AddScoped<RegistrarLancamentoHandler>();
builder.Services.AddScoped<ConsultarLancamentosHandler>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

// Aplica migrations pendentes no startup, controlado por flag de configuração (default: off).
// Por que uma flag, e não sempre aplicar: WebApplicationFactory (testes de integração,
// LancamentosHostTests) sobe o host sem Postgres disponível — migrar incondicionalmente
// acoplaria os testes a um banco real. A flag é ligada só no docker-compose (ambiente
// containerizado, onde `depends_on: postgres: condition: service_healthy` garante o banco de
// pé antes do serviço subir) — aceitável para o tamanho deste projeto/prazo (issue #8).
if (builder.Configuration.GetValue<bool>("Database:MigrateOnStartup"))
{
    using var scope = app.Services.CreateScope();
    var dbContext = scope.ServiceProvider.GetRequiredService<LancamentosDbContext>();
    dbContext.Database.Migrate();
}

app.MapPost("/lancamentos", async (
    RegistrarLancamentoRequest request,
    RegistrarLancamentoHandler handler,
    CancellationToken cancellationToken) =>
{
    try
    {
        var command = new RegistrarLancamentoCommand(request.Data, request.Tipo, request.Valor, request.Descricao);
        var transaction = await handler.Handle(command, cancellationToken);

        return Results.Created($"/lancamentos/{transaction.Id}", RegistrarLancamentoResponse.De(transaction));
    }
    catch (LancamentoInvalidoException ex)
    {
        return Results.BadRequest(new { erro = ex.Message });
    }
});

app.MapGet("/lancamentos", async (
    DateOnly dataInicial,
    DateOnly dataFinal,
    ConsultarLancamentosHandler handler,
    CancellationToken cancellationToken) =>
{
    var command = new ConsultarLancamentosCommand(dataInicial, dataFinal);
    var transactions = await handler.Handle(command, cancellationToken);

    return Results.Ok(transactions.Select(RegistrarLancamentoResponse.De));
});

app.MapGet("/", () => Results.Ok(new { service = "Lancamentos" }));

app.Run();

// Necessário como partial class pública para o WebApplicationFactory<Program> dos testes de
// integração (Lancamentos.Application.Tests) enxergar este Program de fora do assembly.
public partial class Program;
