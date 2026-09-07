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
