# tests

Testes automatizados, espelhando `/src` por camada:

- `Lancamentos.Domain.Tests` / `Consolidado.Domain.Tests` — regras da entidade de domínio e
  fitness de arquitetura (Domain não referencia nenhuma outra camada).
- `Lancamentos.Application.Tests` / `Consolidado.Application.Tests` — resolução de DI (portas de
  Application resolvidas para as implementações de Infrastructure), boot do host via
  `WebApplicationFactory`, e fitness de arquitetura (Application não referencia Infrastructure/Api).
