using System.Net;
using System.Net.Http.Json;
using System.Text;
using Lancamentos.Api;
using Lancamentos.Domain;
using Lancamentos.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Lancamentos.Application.Tests;

/// <summary>
/// Teste de integração de <c>POST /lancamentos</c> (RF01): sobe o host real via
/// <see cref="LancamentosApiFactory"/> (InMemory no lugar de Postgres) e prova o comportamento
/// ponta a ponta — 201 no caminho feliz, 400 com nada persistido no caminho inválido.
/// </summary>
public class RegistrarLancamentoEndpointTests : IDisposable
{
    // Uma factory (e, portanto, um banco InMemory) por teste — não IClassFixture compartilhada —
    // para os testes não interferirem entre si (ex.: "nada é persistido" não pode ver dado de
    // outro teste da mesma classe).
    private readonly LancamentosApiFactory _factory = new();

    public void Dispose() => _factory.Dispose();

    [Fact]
    public async Task Retorna_201_e_persiste_o_lancamento_e_o_evento_de_outbox_quando_valido()
    {
        using var client = _factory.CreateClient();
        var request = new RegistrarLancamentoRequest(new DateOnly(2026, 9, 7), TipoLancamento.Credito, 150.75m, "Venda à vista");

        var response = await client.PostAsJsonAsync("/lancamentos", request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<RegistrarLancamentoResponse>();
        Assert.NotNull(body);
        Assert.NotEqual(Guid.Empty, body!.Id);
        Assert.Equal(request.Valor, body.Valor);

        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<LancamentosDbContext>();
        Assert.Single(dbContext.Transactions, t => t.Id == body.Id);
        Assert.Single(dbContext.OutboxEvents);
    }

    [Fact]
    public async Task Retorna_400_e_nao_persiste_nada_quando_o_valor_e_menor_ou_igual_a_zero()
    {
        using var client = _factory.CreateClient();
        var request = new RegistrarLancamentoRequest(new DateOnly(2026, 9, 7), TipoLancamento.Debito, 0m, "Lançamento inválido");

        var response = await client.PostAsJsonAsync("/lancamentos", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<LancamentosDbContext>();
        Assert.Empty(dbContext.Transactions);
        Assert.Empty(dbContext.OutboxEvents);
    }

    [Fact]
    public async Task Retorna_400_e_nao_persiste_nada_quando_o_tipo_e_invalido()
    {
        using var client = _factory.CreateClient();
        // Envia um valor numérico fora do domínio do enum TipoLancamento (0=Credito, 1=Debito) —
        // um payload que o model binding de um enum válido não bloquearia sozinho, já que
        // System.Text.Json aceita qualquer inteiro para um enum sem validar seus membros.
        var json = """{"data":"2026-09-07","tipo":99,"valor":10,"descricao":"Lançamento inválido"}""";
        using var content = new StringContent(json, Encoding.UTF8, "application/json");

        var response = await client.PostAsync("/lancamentos", content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<LancamentosDbContext>();
        Assert.Empty(dbContext.Transactions);
        Assert.Empty(dbContext.OutboxEvents);
    }
}
