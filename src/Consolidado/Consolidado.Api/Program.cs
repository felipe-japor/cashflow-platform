using Consolidado.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();
builder.Services.AddConsolidadoInfrastructure(builder.Configuration);

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

// Endpoint de consulta do saldo consolidado (RF04) é escopo da issue #14 — aqui só o necessário
// para provar que o host sobe com a infraestrutura corretamente injetada.
app.MapGet("/", () => Results.Ok(new { service = "Consolidado" }));

app.Run();

// Necessário como partial class pública para o WebApplicationFactory<Program> dos testes de
// integração (Consolidado.Application.Tests) enxergar este Program de fora do assembly.
public partial class Program;
