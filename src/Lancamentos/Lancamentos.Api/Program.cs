using Lancamentos.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();
builder.Services.AddLancamentosInfrastructure(builder.Configuration);

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

// Endpoints de negócio (registrar/consultar lançamentos) são escopo da issue #7 — aqui só o
// necessário para provar que o host sobe com a infraestrutura corretamente injetada.
app.MapGet("/", () => Results.Ok(new { service = "Lancamentos" }));

app.Run();

// Necessário como partial class pública para o WebApplicationFactory<Program> dos testes de
// integração (Lancamentos.Application.Tests) enxergar este Program de fora do assembly.
public partial class Program;
