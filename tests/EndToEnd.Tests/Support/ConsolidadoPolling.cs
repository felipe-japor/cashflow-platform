using System.Net;
using System.Net.Http.Json;
using Consolidado.Api;

namespace EndToEnd.Tests.Support;

/// <summary>
/// Polling em <c>GET /consolidado/{data}</c>, compartilhado entre o teste de caminho feliz
/// (issue #18, <see cref="FluxoLancamentoConsolidadoTests"/>) e os de resiliência/isolamento
/// (issue #19, <see cref="ResilienciaIsolamentoServicosTests"/>): os dois precisam esperar o
/// efeito assíncrono de outbox/broker/consumer aparecer no read model antes de assertar, só
/// variando timeout/intervalo conforme o cenário. Extraído aqui para não duplicar a mesma lógica
/// de espera-com-timeout nos dois arquivos de teste (DRY sobre um comportamento que muda pela
/// mesma razão nos dois lugares).
/// </summary>
internal static class ConsolidadoPolling
{
    public static async Task<decimal> ObterCreditosDoDiaAsync(HttpClient consolidadoClient, DateOnly data)
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
    /// Faz polling até <paramref name="creditosEsperados"/> aparecer ou <paramref name="timeout"/>
    /// estourar, caso em que falha com o último valor observado em vez de um timeout mudo.
    /// </summary>
    /// <remarks>
    /// Tolera <see cref="HttpRequestException"/> durante o polling (não deixa propagar) - usado
    /// pelos testes de resiliência (issue #19) logo depois de reiniciar um container via
    /// <c>docker compose start</c>, quando o host ainda está subindo e a conexão pode ser recusada
    /// ou cair no meio (connection reset). Uma falha de conexão nesse momento é esperada, não um
    /// erro real de asserção - só o timeout final é.
    /// </remarks>
    public static async Task<decimal> AguardarCreditosRefletirAsync(
        HttpClient consolidadoClient, DateOnly data, decimal creditosEsperados, TimeSpan timeout, TimeSpan intervalo)
    {
        var prazoFinal = DateTime.UtcNow + timeout;
        decimal? ultimoValorObservado = null;

        while (DateTime.UtcNow < prazoFinal)
        {
            try
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
            }
            catch (HttpRequestException)
            {
                // Container ainda subindo (connection refused/reset) - tenta de novo no próximo
                // ciclo, dentro do mesmo timeout.
            }

            await Task.Delay(intervalo);
        }

        throw new TimeoutException(
            $"Consolidado do dia {data:yyyy-MM-dd} não refletiu o valor esperado dentro de {timeout}. " +
            $"Esperado: {creditosEsperados}, último valor observado: " +
            $"{(ultimoValorObservado?.ToString() ?? "nenhum (404 até o fim do prazo)")}.");
    }
}
