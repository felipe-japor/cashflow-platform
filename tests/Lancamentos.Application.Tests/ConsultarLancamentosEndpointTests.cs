using System.Net;
using System.Net.Http.Json;
using Lancamentos.Api;
using Lancamentos.Domain;

namespace Lancamentos.Application.Tests;

/// <summary>
/// Teste de integração de <c>GET /lancamentos</c> (RF02): sobe o host real via
/// <see cref="LancamentosApiFactory"/> e prova o comportamento ponta a ponta — lançamentos dentro
/// do intervalo retornam ordenados por data, período sem lançamento retorna lista vazia (não
/// erro).
/// </summary>
public class ConsultarLancamentosEndpointTests : IDisposable
{
    private readonly LancamentosApiFactory _factory = new();

    public void Dispose() => _factory.Dispose();

    [Fact]
    public async Task Retorna_200_com_os_lancamentos_do_periodo_ordenados_por_data()
    {
        using var client = _factory.CreateClient();
        var dentroDoIntervalo1 = new RegistrarLancamentoRequest(new DateOnly(2026, 9, 20), TipoLancamento.Debito, 30m, "Segundo dentro");
        var dentroDoIntervalo2 = new RegistrarLancamentoRequest(new DateOnly(2026, 9, 5), TipoLancamento.Credito, 10m, "Primeiro dentro");
        var foraDoIntervalo = new RegistrarLancamentoRequest(new DateOnly(2026, 10, 1), TipoLancamento.Credito, 99m, "Fora do intervalo");
        await client.PostAsJsonAsync("/lancamentos", dentroDoIntervalo1);
        await client.PostAsJsonAsync("/lancamentos", dentroDoIntervalo2);
        await client.PostAsJsonAsync("/lancamentos", foraDoIntervalo);

        var response = await client.GetAsync("/lancamentos?dataInicial=2026-09-01&dataFinal=2026-09-30");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<List<RegistrarLancamentoResponse>>();
        Assert.NotNull(body);
        Assert.Equal(2, body!.Count);
        Assert.Equal(new DateOnly(2026, 9, 5), body[0].Data);
        Assert.Equal(new DateOnly(2026, 9, 20), body[1].Data);
    }

    [Fact]
    public async Task Retorna_200_com_lista_vazia_quando_nao_ha_lancamento_no_periodo()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/lancamentos?dataInicial=2026-01-01&dataFinal=2026-01-31");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<List<RegistrarLancamentoResponse>>();
        Assert.NotNull(body);
        Assert.Empty(body!);
    }
}
