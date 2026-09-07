using System.Net;
using System.Net.Http.Json;
using Consolidado.Api;
using Consolidado.Domain;

namespace Consolidado.Application.Tests;

/// <summary>
/// Teste de integração de <c>GET /consolidado/{data}</c> e <c>GET /consolidado</c> (RF04): sobe o
/// host real via <see cref="ConsolidadoApiFactory"/> e prova o comportamento ponta a ponta.
/// </summary>
public class ConsultarConsolidadoEndpointTests : IDisposable
{
    private readonly ConsolidadoApiFactory _factory = new();

    public void Dispose() => _factory.Dispose();

    [Fact]
    public async Task GetPorData_retorna_200_com_a_posicao_do_dia_quando_ha_lancamentos_processados()
    {
        var data = new DateOnly(2026, 9, 4);
        var consolidado = new DailyConsolidation(data);
        consolidado.Aplicar(TipoLancamento.Credito, 100m);
        consolidado.Aplicar(TipoLancamento.Debito, 30m);
        await _factory.SemearAsync(consolidado);
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/consolidado/2026-09-04");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<ConsolidadoResponse>();
        Assert.NotNull(body);
        Assert.Equal(data, body!.Data);
        Assert.Equal(100m, body.Creditos);
        Assert.Equal(30m, body.Debitos);
        Assert.Equal(70m, body.Saldo);
    }

    [Fact]
    public async Task GetPorData_retorna_404_quando_o_dia_nao_tem_nenhum_lancamento_processado()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/consolidado/2026-09-04");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetPorPeriodo_retorna_200_com_os_consolidados_do_intervalo_ordenados_por_data()
    {
        var dentro1 = new DailyConsolidation(new DateOnly(2026, 9, 20));
        dentro1.Aplicar(TipoLancamento.Debito, 30m);
        var dentro2 = new DailyConsolidation(new DateOnly(2026, 9, 5));
        dentro2.Aplicar(TipoLancamento.Credito, 10m);
        var fora = new DailyConsolidation(new DateOnly(2026, 10, 1));
        fora.Aplicar(TipoLancamento.Credito, 99m);
        await _factory.SemearAsync(dentro1);
        await _factory.SemearAsync(dentro2);
        await _factory.SemearAsync(fora);
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/consolidado?dataInicial=2026-09-01&dataFinal=2026-09-30");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<List<ConsolidadoResponse>>();
        Assert.NotNull(body);
        Assert.Equal(2, body!.Count);
        Assert.Equal(new DateOnly(2026, 9, 5), body[0].Data);
        Assert.Equal(new DateOnly(2026, 9, 20), body[1].Data);
    }

    [Fact]
    public async Task GetPorPeriodo_retorna_200_com_lista_vazia_quando_nenhum_dia_do_intervalo_tem_registro()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/consolidado?dataInicial=2026-01-01&dataFinal=2026-01-31");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<List<ConsolidadoResponse>>();
        Assert.NotNull(body);
        Assert.Empty(body!);
    }
}
