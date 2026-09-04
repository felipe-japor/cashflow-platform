# ADR-002: Padrão Transactional Outbox no serviço de Lançamentos

> Status: aceito.

## Contexto

A ADR-001 decide que a integração entre Lançamentos e Consolidado é via evento assíncrono (`LancamentoRegistrado`). Isso levanta um problema conhecido de integração orientada a eventos: como garantir que a escrita do lançamento (no banco) e a publicação do evento (no broker) aconteçam de forma atômica, dado que são dois sistemas diferentes sem transação distribuída entre eles?

Sem essa garantia, existe o clássico problema de **dual-write**: se o processo grava o lançamento no banco, faz o commit, e falha (crash, restart, erro de rede) antes de publicar o evento no broker, o lançamento existe mas o evento nunca é publicado — o Consolidado nunca fica sabendo daquele lançamento, e o saldo consolidado diverge silenciosamente da fonte da verdade. O inverso (publicar o evento antes do commit e o commit falhar) também é problemático, gerando um evento para um lançamento que não existe. RF01 exige explicitamente que o lançamento seja "persistido e um evento de 'lançamento registrado' seja publicado com atomicidade em relação à escrita".

## Decisão

O serviço de Lançamentos usa o padrão **Transactional Outbox**: ao registrar um lançamento, a aplicação grava, na mesma transação de banco, tanto a entidade `Transaction` quanto um registro na tabela de outbox representando o evento `LancamentoRegistrado` a ser publicado (payload em JSONB, ver ADR-003). Como as duas escritas acontecem na mesma transação relacional, ou as duas são persistidas, ou nenhuma é — nunca um estado intermediário onde o lançamento existe sem o evento correspondente registrado para publicação.

Um publisher separado (worker independente do caminho de escrita da API) lê periodicamente os registros pendentes da tabela de outbox e os publica no broker de mensageria (RabbitMQ local / Azure Service Bus, ADR-004), com retry em caso de falha de publicação, marcando o registro como publicado somente após confirmação do broker.

## Trade-offs considerados

| Alternativa | Por que não foi escolhida |
|---|---|
| Publicar diretamente no broker dentro do próprio handler de escrita do lançamento (sem outbox) | Reintroduz exatamente o problema de dual-write que esta decisão existe para resolver: uma falha do processo entre o commit da transação e a chamada de publicação perde o evento silenciosamente, sem re-tentativa possível, pois nenhum registro do evento pendente sobrevive fora da memória do processo que falhou. |

## Consequências

- É necessário um worker/publisher separado, responsável por ler a tabela de outbox e publicar no broker (issue #10) — não é apenas "chamar o broker" dentro do handler de escrita, adicionando uma peça de infraestrutura a mais no serviço de Lançamentos.
- Existe uma pequena latência adicional entre o commit do lançamento e a publicação efetiva do evento (o tempo até o publisher processar o registro pendente da outbox). Essa latência é aceitável porque o sistema já assume consistência eventual como trade-off deliberado (NFR03, ADR-001) — a outbox apenas adiciona uma fração a mais de segundos a uma defasagem que já existe por natureza da integração assíncrona.
- A tabela de outbox vive no mesmo banco/transação da tabela `Transaction` (detalhe de modelagem tratado na ADR-003), o que reforça a decisão dessa ADR de manter Lançamentos e sua outbox no mesmo banco lógico.
- A garantia é de entrega at-least-once (o publisher pode publicar e falhar em marcar como publicado, gerando reenvio) — coerente com a necessidade de consumo idempotente no Consolidado (NFR04), já prevista na ADR-001.
