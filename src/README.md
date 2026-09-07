# src

Código-fonte da solução — dois serviços independentes, cada um com 4 camadas em projetos
`.csproj` separados (fronteira imposta pelo compilador, decisão de estrutura da issue #6):

```
/Lancamentos
  Lancamentos.Api/            minimal API, DTOs, composition root
  Lancamentos.Application/    casos de uso, portas (ITransactionRepository, IEventPublisher)
  Lancamentos.Domain/         entidade Transaction, invariantes de domínio
  Lancamentos.Infrastructure/ EF Core + Npgsql, outbox, publisher RabbitMQ
/Consolidado
  Consolidado.Api/            minimal API, DTOs, composition root
  Consolidado.Application/    casos de uso, portas (IDailyConsolidationRepository, IEventConsumer)
  Consolidado.Domain/         entidade DailyConsolidation, invariantes de domínio
  Consolidado.Infrastructure/ EF Core + Npgsql, inbox, consumer RabbitMQ
/Shared
  Shared.Contracts/           DTO do evento LancamentoRegistrado — referenciado só pelas duas Infrastructure
```

Ver `docs/domain-mapping.md` e `docs/architecture.md` para o racional completo.
