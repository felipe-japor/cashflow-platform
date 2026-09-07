using System.Net;
using System.Net.Http.Json;
using Consolidado.Api;
using Lancamentos.Api;
using Lancamentos.Domain;

namespace EndToEnd.Tests;

/// <summary>
/// Prova o caminho ponta a ponta real do fluxo de caixa (issue #18): lançamento registrado via
/// HTTP -&gt; outbox transacional -&gt; RabbitMQ -&gt; consumer idempotente -&gt; consolidado
/// diário atualizado. Roda contra os containers de longa duração do <c>docker-compose.yml</c>
/// (Postgres e RabbitMQ reais) - não sobe nem derruba nada (ver <c>tests/EndToEnd.Tests/README.md</c>
/// para como rodar) e, por depender de timing de Docker, não entra no <c>dotnet test</c> da
/// rotina normal (projeto fora de <c>CashFlowPlatform.sln</c>, marcado com a trait
/// <c>Category=Integration</c> só como documentação adicional).
///
/// Escopo deliberadamente mínimo: só o caminho feliz. Regras de negócio e casos de borda de
/// payload já são cobertos pelos testes de unidade/componente (*.Domain.Tests,
/// *.Application.Tests) - diretriz fechada na issue #18, não reaberta aqui.
/// </summary>
[Trait("Category", "Integration")]
public class FluxoLancamentoConsolidadoTests
{
    private static readonly Uri LancamentosBaseUrl = new("http://localhost:5101");
    private static readonly Uri ConsolidadoBaseUrl = new("http://localhost:5102");
    private static readonly TimeSpan PollingTimeout = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan PollingInterval = TimeSpan.FromMilliseconds(500);

    [Fact]
    public async Task Lancamento_registrado_deve_refletir_no_consolidado_do_dia_via_outbox_e_broker()
    {
        using var lancamentosClient = new HttpClient { BaseAddress = LancamentosBaseUrl };
        using var consolidadoClient = new HttpClient { BaseAddress = ConsolidadoBaseUrl };

        var data = DateOnly.FromDateTime(DateTime.UtcNow);
        const decimal valor = 111.11m;

        // Baseline: quanto o consolidado do dia já mostra ANTES deste lançamento. Comparar por
        // delta (em vez de valor absoluto) mantém o teste correto mesmo rodando várias vezes no
        // mesmo dia contra o mesmo Postgres de longa duração, sem depender de reset de estado
        // entre execuções (os containers não são recriados por este teste - diretriz da issue #18).
        var creditosAntes = await ObterCreditosDoDiaAsync(consolidadoClient, data);
        var creditosEsperados = creditosAntes + valor;

        var request = new RegistrarLancamentoRequest(data, TipoLancamento.Credito, valor, "Teste E2E issue #18");
        var response = await lancamentosClient.PostAsJsonAsync("/lancamentos", request);
        response.EnsureSuccessStatusCode();

        var creditosDepois = await AguardarConsolidadoRefletirAsync(consolidadoClient, data, creditosEsperados);

        Assert.Equal(creditosEsperados, creditosDepois);
    }

    private static async Task<decimal> ObterCreditosDoDiaAsync(HttpClient consolidadoClient, DateOnly data)
    {
        var response = await consolidadoClient.GetAsync($"/consolidado/{data:yyyy-MM-dd}");
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return 0m;
        }

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<ConsolidadoResponse>();
        return body!.Creditos;
    }

    /// <summary>
    /// Faz polling em <c>GET /consolidado/{data}</c> até o valor de créditos esperado aparecer -
    /// outbox, broker e consumer são assíncronos, então o efeito do POST não é imediato - ou o
    /// timeout estourar, caso em que falha com o último valor observado em vez de um timeout mudo.
    /// </summary>
    private static async Task<decimal> AguardarConsolidadoRefletirAsync(
        HttpClient consolidadoClient, DateOnly data, decimal creditosEsperados)
    {
        var prazoFinal = DateTime.UtcNow + PollingTimeout;
        decimal? ultimoValorObservado = null;

        while (DateTime.UtcNow < prazoFinal)
        {
            var response = await consolidadoClient.GetAsync($"/consolidado/{data:yyyy-MM-dd}");
            if (response.StatusCode == HttpStatusCode.OK)
            {
                var body = await response.Content.ReadFromJsonAsync<ConsolidadoResponse>();
                ultimoValorObservado = body!.Creditos;
                if (ultimoValorObservado == creditosEsperados)
                {
                    return ultimoValorObservado.Value;
                }
            }

            await Task.Delay(PollingInterval);
        }

        throw new TimeoutException(
            $"Consolidado do dia {data:yyyy-MM-dd} não refletiu o lançamento dentro de {PollingTimeout}. " +
            $"Esperado: {creditosEsperados}, último valor observado: " +
            $"{(ultimoValorObservado?.ToString() ?? "nenhum (404 até o fim do prazo)")}.");
    }
}
