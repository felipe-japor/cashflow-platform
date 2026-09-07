# EndToEnd.Tests

Teste de integração ponta a ponta (issue #18): lançamento -> outbox -> broker -> consolidado
atualizado. Prova o caminho real - HTTP real, outbox transacional real, RabbitMQ real, consumer
idempotente real, Postgres real - sem simular nenhuma dessas peças (sem Testcontainers, sem
`WebApplicationFactory`).

## Por que este projeto fica fora de `CashFlowPlatform.sln`

Diretriz fechada na issue #18 (discutida com o testerAgent, aprovada por Felipe): este teste não
pode entrar na rotina rápida de `dotnet test` do dia a dia, porque depende de containers Docker de
longa duração já no ar e de timing real de rede/mensageria - rodá-lo junto da suíte normal a
tornaria lenta e flaky. Por isso o projeto não é referenciado por `CashFlowPlatform.sln` (um
`dotnet test`/`dotnet test CashFlowPlatform.sln` na raiz nunca o executa); a trait
`[Trait("Category", "Integration")]` na classe de teste é só documentação adicional para quem abrir
o projeto sozinho. Mesma recomendação vale para as issues #19/#20.

## Como rodar

1. Suba os containers de longa duração na raiz do repo (se ainda não estiverem no ar):

   ```
   docker compose up --build -d
   ```

2. Rode este projeto isoladamente:

   ```
   dotnet test tests/EndToEnd.Tests/EndToEnd.Tests.csproj
   ```

O teste fala HTTP direto com `lancamentos-api` (`http://localhost:5101`) e `consolidado-api`
(`http://localhost:5102`), as portas publicadas pelo `docker-compose.yml`.

## Escopo

Só o caminho feliz: registra um lançamento de crédito para o dia corrente via
`POST /lancamentos` e faz polling (timeout de 20s) em `GET /consolidado/{data}` até o valor
esperado aparecer. Regras de negócio e casos de borda de payload não são reexplorados aqui - já são
cobertos pelos testes de unidade/componente em `*.Domain.Tests` e `*.Application.Tests`.

A asserção compara por **delta** (créditos antes/depois do POST), não por valor absoluto - os
containers são de longa duração e o Postgres não é resetado entre execuções deste teste, então
rodar mais de uma vez no mesmo dia não deve quebrar a asserção.

## `ResilienciaIsolamentoServicosTests` (issue #19)

Prova direta do NFR "Lançamentos não pode cair se Consolidado/o broker cair". Diferente do teste
de caminho feliz acima, este derruba e restaura containers de verdade via `docker compose
stop`/`start` (`rabbitmq` e `consolidado-api`) - nunca mock de exceção. Cada cenário:

1. Derruba o container do serviço dependente.
2. Confirma que `POST /lancamentos` continua respondendo 2xx.
3. Restaura o container (em `finally`, mesmo se a asserção anterior falhar).
4. Faz polling em `GET /consolidado/{data}` até o lançamento aceito durante a indisponibilidade
   aparecer sozinho no consolidado - sem nenhum endpoint de "reprocessar" manual.

Ver os comentários XML de `ResilienciaIsolamentoServicosTests.cs` para o detalhe de qual falha é
simulada em cada cenário (broker vs. Consolidado) e por que o Postgres compartilhado (ADR-003) não
é derrubado como forma de simular "Consolidado indisponível".

Assim como o teste acima, requer os containers já no ar (`docker compose up --build -d`) e não
entra no `dotnet test` de rotina - rode isoladamente:

```
dotnet test tests/EndToEnd.Tests/EndToEnd.Tests.csproj --filter "FullyQualifiedName~ResilienciaIsolamentoServicosTests"
```

**Atenção ao rodar localmente**: este teste para e reinicia containers do `docker-compose.yml` do
próprio ambiente onde ele roda. Não rode em paralelo com outra suíte que dependa desses mesmos
containers estarem sempre no ar.

## `ConsolidadoConsultaLoadTests` (issue #20)

Valida o SLA explícito do desafio - 50 req/s de pico, no máximo 5% de perda - contra
`GET /consolidado/{data}` (RF04, issue #14), o único endpoint com SLA numérico. Ferramenta:
[NBomber](https://nbomber.com/) (decisão fechada na issue #20 com o testerAgent e Felipe) - roda
como um `[Fact]` xUnit comum que registra e executa o cenário de carga via `NBomberRunner`, sem
exigir binário externo (k6 ficou fora, fora do stack .NET) nem reinventar medição de
taxa/percentil na mão (script cru com `HttpClient`+`Parallel.ForEach`).

Cenário único, carga **constante** (não spike, não ramp-up, não soak test longo - fora do escopo
da issue): injeta exatamente 50 requisições/segundo (`Simulation.Inject`, não `KeepConstant`, que
controla concorrência de "cópias" e não requisições/segundo) por 30s contra o dia corrente. Antes
de iniciar a carga, o teste registra um lançamento de setup via `POST /lancamentos` e faz o mesmo
polling de `ConsolidadoPolling` (issue #18) até ele refletir no consolidado - sem isso,
`GET /consolidado/{data}` responderia 404 sistematicamente (dia sem lançamento processado) e
infla artificialmente a taxa de "erro" medida sem relação nenhuma com o SLA de fato.

Sem Testcontainers (mesma decisão de #18/#19) e sem infraestrutura de dashboard/observabilidade
de carga (Grafana etc.) - o relatório nativo do NBomber (Markdown/texto, gravado em
`tests/EndToEnd.Tests/bin/Debug/net10.0/reports/` a cada execução) já é evidência suficiente. As
métricas mínimas (taxa de erro/perda e latência p50/p95/p99) também aparecem no console e na
saída do teste (`dotnet test ... --logger "console;verbosity=detailed"`).

Requer os containers já no ar (`docker compose up --build -d`) - roda isoladamente:

```
dotnet test tests/EndToEnd.Tests/EndToEnd.Tests.csproj --filter "FullyQualifiedName~ConsolidadoConsultaLoadTests"
```
