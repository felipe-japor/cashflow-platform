# Mapeamento de Domínios e Capacidades de Negócio

> Base para o desenho da solução (#3, #5) e para a implementação dos serviços (#6-#14). Decisões de modelagem de dado que decorrem daqui (motor de banco, estratégia de chave primária) estão detalhadas em `docs/adr/ADR-003-database-strategy.md`.

## Domínios identificados

### Domínio 1 — Lançamentos (Cash Flow Ledger)

Domínio **upstream**, write model e fonte da verdade do fluxo de caixa. É o domínio central (core domain) do desafio: sem lançamento registrado não há o que consolidar.

**Capacidades de negócio:**

- **Registrar lançamento** — ação de negócio real de criar um lançamento de débito ou crédito (RF01). Não é "receber lançamento": o serviço valida e persiste, não apenas repassa dado.
- **Consultar lançamentos** — permite ao comerciante conferir o detalhe das movimentações que compõem o saldo (RF02).

**Entidade: `Transaction`**

| Campo | Tipo | Observação |
|---|---|---|
| `Id` | `UUID` | Identidade técnica própria — agregado com lifecycle independente, é o identificador referenciado pelo evento de domínio publicado para o Consolidado. |
| `Data` | `DateOnly` | Dia de negócio do lançamento, em UTC — ver decisão "Corte de dia em UTC" abaixo. |
| `Tipo` | enum | Crédito ou débito. |
| `Valor` | decimal | Valor do lançamento (> 0 — o sinal do tipo, não do valor, define crédito/débito). |
| `Descricao` | string | Descrição livre do lançamento. |

### Domínio 2 — Consolidado Diário (Daily Balance)

Domínio **downstream**, read model. Não é dono de nenhuma regra de negócio sobre o que é um lançamento válido — sua única responsabilidade é manter, por dia, o resultado agregado dos lançamentos que já foram publicados pelo domínio Lançamentos.

**Capacidades de negócio:**

- **Consolidar saldo diário** — engloba nettar créditos e débitos do dia a partir dos eventos consumidos (RF03). Creditar e debitar não são capacidades de negócio separadas aqui: é uma única operação de consolidação incremental por evento.
- **Consultar saldo consolidado por data** — relatório de saldo diário, por data ou por período (RF04).

**Entidade: `DailyConsolidation`**

| Campo | Tipo | Observação |
|---|---|---|
| `Id` | `UUID` | Chave primária, identidade técnica separada da regra de negócio. Convive com o índice único em `Data` — não o substitui (ver ADR-003 para o racional completo da escolha de UUID como PK). |
| `Data` | `DateOnly` | Chave de negócio: um registro por dia, garantido por índice único. Dia de negócio em UTC — mesma decisão do domínio Lançamentos. |
| `Creditos` | decimal | Soma acumulada dos créditos do dia. Campo armazenado, não calculado on-the-fly. |
| `Debitos` | decimal | Soma acumulada dos débitos do dia. Campo armazenado. |
| `Saldo` | decimal | `Creditos - Debitos` do dia. Campo armazenado. |

`Creditos`, `Debitos` e `Saldo` são atualizados **incrementalmente** a cada evento consumido — o Consolidado não tem acesso às `Transaction`s brutas do outro serviço, apenas ao conteúdo do evento `LancamentoRegistrado`. Calcular o saldo on-the-fly a partir de lançamentos brutos exigiria o Consolidado consultar (síncrona ou assincronamente) o volume completo de lançamentos do dia a cada requisição, o que viola o isolamento de disponibilidade (NFR01) e não sustenta a carga de 50 req/s (NFR02). Manter um read model materializado, atualizado incrementalmente, é o que torna a consulta (RF04) uma leitura simples de um único registro por data.

**Nota de idempotência (conceito de domínio, implementação na issue #12):**

Soma incremental (`saldo += valor`) **não é idempotente por si só** — reprocessar o mesmo evento duas vezes sempre altera o estado (soma de novo). A garantia at-least-once do broker (ADR-001) significa que reentrega é esperada, não excepcional (NFR04). A idempotência real do consumo vem de um **inbox pattern**: uma tabela de eventos processados, com constraint única em `event_id`, onde a aplicação do delta (`saldo += valor`) e o registro do evento como processado acontecem **na mesma transação atômica** — "se `event_id` já está na tabela de processados, ignora (skip); senão, aplica o delta e marca como processado, atomicamente". Isso pertence ao mapeamento do domínio porque é uma regra de negócio (mesmo lançamento não pode ser contado duas vezes no saldo), não um detalhe de infraestrutura — a implementação concreta (schema da tabela, transação) é escopo da issue #12.

## Evento de domínio: `LancamentoRegistrado`

Terceiro conceito do domínio, que atravessa os dois bounded contexts: é o **contrato de integração** entre Lançamentos (produtor) e Consolidado (consumidor). Nome escolhido em português (`LancamentoRegistrado`) para manter consistência com a ubiquitous language do domínio de negócio (comerciante, lançamento, fluxo de caixa) usada no restante do projeto, em vez de traduzir para inglês (`TransactionRegistered`) — escolha de nomenclatura, sem impacto técnico.

Carrega exatamente os dados necessários para o Consolidado atualizar o read model sem precisar consultar o serviço de Lançamentos:

| Campo | Tipo | Motivo de estar no evento |
|---|---|---|
| `TransactionId` | `UUID` | Rastreabilidade e chave de idempotência (inbox pattern, ver acima) — cada `event_id` processado referencia o lançamento de origem. |
| `Data` | `DateOnly` | Determina em qual `DailyConsolidation` o delta é aplicado. |
| `Tipo` | enum | Determina se o delta soma em `Creditos` ou `Debitos`. |
| `Valor` | decimal | O delta em si. |

Publicado via **outbox transacional** no serviço de Lançamentos (ADR-002) — atomicidade entre a escrita do lançamento e a publicação do evento, sem depender de o broker estar disponível no momento da escrita (sustenta NFR01). Consumido de forma idempotente pelo Consolidado via inbox pattern (nota acima, implementação na issue #12).

## Decisão: corte de dia em UTC + tipo `DateOnly`

O "dia" de negócio — tanto `Transaction.Data` quanto `DailyConsolidation.Data` — é definido em **UTC**. Decisão explícita, não implícita.

**Motivo:** nenhum requisito do desafio menciona comerciante multi-fuso ou fuso horário configurável. Modelar suporte a fuso agora seria generalizar para um caso hipotético, o que viola KISS — a solução mais simples que resolve o problema real, não a mais genérica possível.

**Como funciona na prática:** a conversão de um timestamp real (o instante em que o evento de fato aconteceu, com fuso/hora) para "qual dia de negócio esse lançamento pertence" acontece **uma única vez, na borda** — no momento de registrar o lançamento (Lançamentos) e, simetricamente, ao publicar/consumir o evento. A partir daí, `Data` é um `DateOnly` puro: sem componente de hora, sem fuso, sem ambiguidade. Isso elimina uma classe inteira de bugs de "lançamento caiu no dia errado por causa de fuso" que apareceria se `Data` fosse `DateTime`/`DateTimeOffset` carregado adiante pelas camadas do sistema.

Se o requisito de fuso configurável por comerciante aparecer no futuro, é uma mudança de escopo real, tratada quando (e se) for pedida — não uma antecipação de complexidade hoje.

## Mapa de contexto (Context Map)

```
┌───────────────────────┐         evento          ┌───────────────────────┐
│  Domínio Lançamentos   │   LancamentoRegistrado   │  Domínio Consolidado  │
│  (upstream)            │ ────────────────────────>│  (downstream)         │
│  write model /         │   via outbox transacional │  read model           │
│  fonte da verdade      │   (ADR-002), broker        │  (materializado,      │
│                        │   assíncrono (ADR-001)     │  atualizado por evento)│
└───────────────────────┘                            └───────────────────────┘
```

- **Lançamentos → Consolidado**: relação **upstream/downstream** — Lançamentos não sabe que o Consolidado existe, não depende dele, não faz chamada síncrona para ele em nenhuma circunstância. Publica o evento na própria outbox e segue.
- **Consolidado → Lançamentos**: nenhuma chamada em nenhuma direção. O Consolidado não consulta o serviço de Lançamentos para montar seu read model — só consome o evento.
- **Natureza da integração**: assíncrona, via evento, at-least-once, com consistência eventual (NFR03) como trade-off deliberado em troca de isolamento de disponibilidade (NFR01) — se o Consolidado cair, o Lançamentos continua operando normalmente; os eventos se acumulam na outbox/broker até o Consolidado voltar a consumi-los.
