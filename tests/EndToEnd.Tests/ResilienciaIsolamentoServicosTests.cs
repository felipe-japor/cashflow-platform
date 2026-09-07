using System.Net.Http.Json;
using EndToEnd.Tests.Support;
using Lancamentos.Api;
using Lancamentos.Domain;

namespace EndToEnd.Tests;

/// <summary>
/// Prova direta do requisito não-funcional central do desafio (issue #19): "Lançamentos não pode
/// cair se Consolidado/o broker cair". Simula, com containers Docker reais de longa duração (os
/// mesmos do <c>docker-compose.yml</c>, reaproveitados da issue #18) - nunca mock de exceção -
/// dois cenários de indisponibilidade e comprova, para cada um:
///
/// <list type="number">
/// <item><description><c>POST /lancamentos</c> continua respondendo 2xx com o serviço dependente
/// fora do ar - a escrita fica só na transação Postgres local de Lançamentos (outbox transacional,
/// ADR-002), sem chamada síncrona a broker ou a Consolidado.</description></item>
/// <item><description>Depois do container restaurado, o lançamento aceito durante a
/// indisponibilidade aparece sozinho no consolidado do dia - sem nenhum endpoint de "reprocessar"
/// manual, que não existe e não foi criado só para este teste.</description></item>
/// </list>
///
/// Dois cenários cobrem o NFR (decisão fechada na issue, não uma matriz de combinações de falha):
///
/// <list type="bullet">
/// <item><description><b>Broker indisponível</b> (<c>docker compose stop rabbitmq</c>): o
/// <c>OutboxPublisherWorker</c> tenta publicar, falha, e tenta de novo no próximo poll (o próprio
/// ciclo de polling É o retry - issue #10, sem backoff configurável separado). Quando o broker
/// volta, a reconexão automática do RabbitMQ.Client (<c>AutomaticRecoveryEnabled</c>, ligado por
/// padrão) mais o próximo poll do worker publicam o evento sem nenhuma ação manual.</description></item>
/// <item><description><b>Consolidado indisponível</b> (<c>docker compose stop consolidado-api</c>):
/// o broker RabbitMQ continua no ar, então a mensagem é publicada normalmente e fica retida na
/// fila durável (issue #9/#10) enquanto não há consumidor. Quando o container do Consolidado
/// volta, o <c>EventConsumerWorker</c> reconecta e drena a fila sozinho - sem replay
/// manual.</description></item>
/// </list>
///
/// Deliberadamente <b>não</b> simula o Postgres do lado do Consolidado (embora a diretriz da issue
/// permitisse essa alternativa): a instância de Postgres é compartilhada entre os dois serviços
/// (ADR-003, mesma instância/bancos lógicos distintos). Derrubar o Postgres compartilhado também
/// tiraria a escrita de Lançamentos do ar (que depende do seu próprio banco lógico na mesma
/// instância) - isso provaria "Lançamentos precisa do próprio Postgres", não "Lançamentos é imune a
/// uma falha do Consolidado", que é o que o NFR pede. Derrubar o container <c>consolidado-api</c>
/// isola exatamente a falha do serviço dependente, sem essa confusão.
///
/// Roda contra os containers de longa duração do <c>docker-compose.yml</c> (ver
/// <c>tests/EndToEnd.Tests/README.md</c>) - mesmo racional de execução sob demanda, fora do
/// <c>dotnet test</c> de rotina, da issue #18. Cada cenário restaura o container que derrubou em
/// um <c>finally</c>, mesmo que a asserção intermediária falhe, para não deixar o ambiente
/// inconsistente entre execuções.
/// </summary>
[Trait("Category", "Integration")]
public class ResilienciaIsolamentoServicosTests
{
    private static readonly Uri LancamentosBaseUrl = new("http://localhost:5101");
    private static readonly Uri ConsolidadoBaseUrl = new("http://localhost:5102");

    // Mais generoso que o timeout do teste de caminho feliz (issue #18, 20s): aqui, além do poll
    // do OutboxPublisherWorker (5s em produção - appsettings.json - e é essa mesma configuração
    // que roda aqui, sem afrouxar/acelerar para o teste), a janela ainda precisa cobrir o tempo de
    // reinício do container derrubado e a reconexão do cliente RabbitMQ. O timeout é paciência de
    // asserção, não uma política de retry alternativa - a política de retry real é a de produção.
    private static readonly TimeSpan DrainPollingTimeout = TimeSpan.FromSeconds(45);
    private static readonly TimeSpan DrainPollingInterval = TimeSpan.FromSeconds(1);

    [Fact]
    public async Task Lancamentos_aceita_POST_com_broker_RabbitMQ_indisponivel_e_evento_drena_sozinho_quando_broker_volta()
    {
        using var lancamentosClient = new HttpClient { BaseAddress = LancamentosBaseUrl };
        using var consolidadoClient = new HttpClient { BaseAddress = ConsolidadoBaseUrl };

        var data = DateOnly.FromDateTime(DateTime.UtcNow);
        const decimal valor = 222.22m;

        var creditosAntes = await ConsolidadoPolling.ObterCreditosDoDiaAsync(consolidadoClient, data);
        var creditosEsperados = creditosAntes + valor;

        await DockerCompose.StopAsync("rabbitmq");
        try
        {
            var request = new RegistrarLancamentoRequest(
                data, TipoLancamento.Credito, valor, "Teste resiliência issue #19 - broker indisponível");
            var response = await lancamentosClient.PostAsJsonAsync("/lancamentos", request);

            // Asserção 1: a escrita não depende do broker - mesma transação Postgres do outbox
            // (ADR-002/issue #9), então o POST responde 2xx normalmente mesmo com o RabbitMQ fora
            // do ar.
            response.EnsureSuccessStatusCode();
        }
        finally
        {
            await DockerCompose.StartAsync("rabbitmq");
        }

        // Asserção 2: sem nenhuma ação manual, o evento pendente drena sozinho assim que o broker
        // volta - via retry do próprio ciclo de poll do OutboxPublisherWorker (issue #10).
        var creditosDepois = await ConsolidadoPolling.AguardarCreditosRefletirAsync(
            consolidadoClient, data, creditosEsperados, DrainPollingTimeout, DrainPollingInterval);

        Assert.Equal(creditosEsperados, creditosDepois);
    }

    [Fact]
    public async Task Lancamentos_aceita_POST_com_Consolidado_indisponivel_e_evento_drena_sozinho_quando_Consolidado_volta()
    {
        using var lancamentosClient = new HttpClient { BaseAddress = LancamentosBaseUrl };
        using var consolidadoClient = new HttpClient { BaseAddress = ConsolidadoBaseUrl };

        var data = DateOnly.FromDateTime(DateTime.UtcNow);
        const decimal valor = 333.33m;

        // Baseline lido ANTES de derrubar o consolidado-api - com o container fora do ar, GET
        // /consolidado não responde.
        var creditosAntes = await ConsolidadoPolling.ObterCreditosDoDiaAsync(consolidadoClient, data);
        var creditosEsperados = creditosAntes + valor;

        await DockerCompose.StopAsync("consolidado-api");
        try
        {
            var request = new RegistrarLancamentoRequest(
                data, TipoLancamento.Credito, valor, "Teste resiliência issue #19 - Consolidado indisponível");
            var response = await lancamentosClient.PostAsJsonAsync("/lancamentos", request);

            // Asserção 1: Lançamentos não chama o Consolidado de forma síncrona - o POST responde
            // 2xx normalmente mesmo com o serviço dependente inteiro fora do ar.
            response.EnsureSuccessStatusCode();
        }
        finally
        {
            await DockerCompose.StartAsync("consolidado-api");
        }

        // Asserção 2: sem nenhuma ação manual, o evento drena sozinho assim que o Consolidado
        // volta - aqui o RabbitMQ (que nunca saiu do ar neste cenário) já tinha aceitado e retido a
        // mensagem na fila durável; o EventConsumerWorker só precisa reconectar e consumir o que já
        // estava esperando.
        var creditosDepois = await ConsolidadoPolling.AguardarCreditosRefletirAsync(
            consolidadoClient, data, creditosEsperados, DrainPollingTimeout, DrainPollingInterval);

        Assert.Equal(creditosEsperados, creditosDepois);
    }
}
