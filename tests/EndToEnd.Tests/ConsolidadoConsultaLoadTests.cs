using System.Net.Http.Json;
using EndToEnd.Tests.Support;
using Lancamentos.Api;
using Lancamentos.Domain;
using NBomber.Contracts;
using NBomber.Contracts.Stats;
using NBomber.CSharp;
using Xunit.Abstractions;

namespace EndToEnd.Tests;

/// <summary>
/// Teste de carga (issue #20): valida o SLA explícito do desafio - 50 req/s de pico, no máximo
/// 5% de perda - contra <c>GET /consolidado/{data}</c> (RF04, issue #14), o único endpoint com
/// SLA numérico. Roda contra os containers de longa duração do <c>docker-compose.yml</c> (mesma
/// infra e mesmas convenções de <see cref="FluxoLancamentoConsolidadoTests"/> e
/// <see cref="ResilienciaIsolamentoServicosTests"/>) - sem Testcontainers, sem dashboard de
/// observabilidade de carga (Grafana etc.): o relatório nativo do NBomber (Markdown/HTML,
/// gravado em <c>tests/EndToEnd.Tests/reports/</c>) já é evidência suficiente, decisão fechada na
/// issue.
///
/// Cenário único, carga constante (não spike, não ramp-up): fora do escopo desta issue explorar
/// outros padrões de carga - o desafio pede um número específico de req/s sustentado, um cenário
/// já responde à pergunta.
/// </summary>
[Trait("Category", "Load")]
public class ConsolidadoConsultaLoadTests
{
    private static readonly Uri LancamentosBaseUrl = new("http://localhost:5101");
    private static readonly Uri ConsolidadoBaseUrl = new("http://localhost:5102");
    private static readonly TimeSpan PollingTimeout = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan PollingInterval = TimeSpan.FromMilliseconds(500);

    private const int TaxaAlvoReqsPorSegundo = 50;
    private static readonly TimeSpan DuracaoCarga = TimeSpan.FromSeconds(30);
    private const double PerdaMaximaAceitavel = 0.05;

    private readonly ITestOutputHelper _output;

    public ConsolidadoConsultaLoadTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task Consulta_ao_consolidado_do_dia_deve_sustentar_50_reqs_por_segundo_com_ate_5_por_cento_de_perda()
    {
        var data = await GarantirDiaComConsolidadoPopuladoAsync();

        using var consolidadoClient = new HttpClient
        {
            BaseAddress = ConsolidadoBaseUrl,
            Timeout = TimeSpan.FromSeconds(5),
        };

        // Simulação por injeção de taxa (não KeepConstant, que controla concorrência de "cópias",
        // não requisições/segundo): "rate: 50, interval: 1s" injeta exatamente 50 requisições por
        // segundo, o mesmo jeito que o SLA do desafio é enunciado.
        var cenario = Scenario.Create("consultar_consolidado_dia", async _ =>
        {
            var response = await consolidadoClient.GetAsync($"/consolidado/{data:yyyy-MM-dd}");
            return response.IsSuccessStatusCode
                ? Response.Ok(statusCode: ((int)response.StatusCode).ToString())
                : Response.Fail(statusCode: ((int)response.StatusCode).ToString());
        })
        .WithoutWarmUp()
        .WithLoadSimulations(
            Simulation.Inject(rate: TaxaAlvoReqsPorSegundo, interval: TimeSpan.FromSeconds(1), during: DuracaoCarga));

        NodeStats stats = NBomberRunner
            .RegisterScenarios(cenario)
            .WithReportFolder("reports")
            .WithReportFormats(ReportFormat.Md, ReportFormat.Txt)
            .Run();

        var scenarioStats = stats.ScenarioStats.Single(s => s.ScenarioName == cenario.ScenarioName);
        var totalRequisicoes = scenarioStats.Ok.Request.Count + scenarioStats.Fail.Request.Count;
        var taxaDePerda = totalRequisicoes == 0 ? 1.0 : (double)scenarioStats.Fail.Request.Count / totalRequisicoes;

        _output.WriteLine(
            $"Requisições: total={totalRequisicoes}, ok={scenarioStats.Ok.Request.Count}, " +
            $"fail={scenarioStats.Fail.Request.Count}, perda={taxaDePerda:P2}");
        _output.WriteLine(
            $"Latência (ms) ok: p50={scenarioStats.Ok.Latency.Percent50}, " +
            $"p95={scenarioStats.Ok.Latency.Percent95}, p99={scenarioStats.Ok.Latency.Percent99}");

        Assert.True(totalRequisicoes > 0, "Nenhuma requisição foi executada pelo cenário de carga.");
        Assert.True(
            taxaDePerda <= PerdaMaximaAceitavel,
            $"Taxa de perda {taxaDePerda:P2} excede o SLA de {PerdaMaximaAceitavel:P0} " +
            $"({scenarioStats.Fail.Request.Count} de {totalRequisicoes} requisições falharam).");
    }

    /// <summary>
    /// Garante que o dia corrente tenha ao menos um lançamento já refletido no consolidado antes
    /// de iniciar a carga - sem isso, <c>GET /consolidado/{data}</c> responderia 404
    /// sistematicamente (dia sem lançamento processado, ver <c>Consolidado.Api/Program.cs</c>),
    /// o que infla artificialmente a taxa de "erro" medida sem nenhuma relação com o SLA de fato
    /// (que é sobre um endpoint servindo dado real, não sobre 404 esperado). Reaproveita o mesmo
    /// polling de <see cref="FluxoLancamentoConsolidadoTests"/> (issue #18) para confirmar que o
    /// lançamento de setup propagou via outbox/broker antes de disparar a carga.
    /// </summary>
    private static async Task<DateOnly> GarantirDiaComConsolidadoPopuladoAsync()
    {
        using var lancamentosClient = new HttpClient { BaseAddress = LancamentosBaseUrl };
        using var consolidadoClient = new HttpClient { BaseAddress = ConsolidadoBaseUrl };

        var data = DateOnly.FromDateTime(DateTime.UtcNow);
        const decimal valor = 1.00m;

        var creditosAntes = await ConsolidadoPolling.ObterCreditosDoDiaAsync(consolidadoClient, data);
        var creditosEsperados = creditosAntes + valor;

        var request = new RegistrarLancamentoRequest(data, TipoLancamento.Credito, valor, "Setup teste de carga issue #20");
        var response = await lancamentosClient.PostAsJsonAsync("/lancamentos", request);
        response.EnsureSuccessStatusCode();

        await ConsolidadoPolling.AguardarCreditosRefletirAsync(
            consolidadoClient, data, creditosEsperados, PollingTimeout, PollingInterval);

        return data;
    }
}
