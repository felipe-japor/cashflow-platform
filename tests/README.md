# tests

Testes automatizados, espelhando `/src` por camada:

- `Lancamentos.Domain.Tests` / `Consolidado.Domain.Tests` — regras da entidade de domínio e
  fitness de arquitetura (Domain não referencia nenhuma outra camada).
- `Lancamentos.Application.Tests` / `Consolidado.Application.Tests` — resolução de DI (portas de
  Application resolvidas para as implementações de Infrastructure), boot do host via
  `WebApplicationFactory`, e fitness de arquitetura (Application não referencia Infrastructure/Api).
- `Lancamentos.Infrastructure.Tests` / `Consolidado.Infrastructure.Tests` — comportamento real das
  implementações de repositório (`TransactionRepository`/`DailyConsolidationRepository`): outbox
  transacional e idempotência via inbox, via EF Core InMemory (sem Postgres). Não cobre os
  publishers/consumers RabbitMQ, que abrem I/O de verdade e ficam para teste de integração com
  broker real (issues #10/#11/#12).
