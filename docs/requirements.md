# Requisitos Funcionais e Não Funcionais

> Refinado a partir do desafio original (issue #1). Serve de base para o mapeamento de domínios (#2) e para a arquitetura-alvo (#3).

## Requisitos funcionais

### Serviço de Lançamentos

**RF01 — Registrar lançamento**

Como comerciante, quero registrar um lançamento de débito ou crédito no meu fluxo de caixa, para manter o controle diário das minhas movimentações financeiras.

Critérios de aceite:
- Dado um lançamento válido (tipo débito ou crédito, valor > 0, data), quando ele é registrado, então é persistido e um evento de "lançamento registrado" é publicado com atomicidade em relação à escrita (outbox transacional — ADR-002).
- Dado um lançamento com valor ≤ 0 ou tipo inválido, quando submetido, então o serviço rejeita com erro de validação e nada é persistido.
- Dado o serviço de Consolidado Diário indisponível, quando um lançamento é registrado, então o registro é concluído normalmente — não há chamada síncrona entre os serviços, o evento aguarda na outbox até ser publicado com sucesso.

**RF02 — Consultar lançamentos**

Como comerciante, quero consultar os lançamentos já registrados, para conferir o detalhe das movimentações que compõem meu saldo.

Critérios de aceite:
- Dado lançamentos já registrados, quando consultados por um intervalo de datas, então a lista retornada contém exatamente os lançamentos daquele intervalo, ordenados por data.
- Dado nenhum lançamento no período informado, quando a consulta é feita, então o retorno é uma lista vazia (não um erro).

### Serviço de Consolidado Diário

**RF03 — Consolidar saldo diário a partir dos lançamentos**

Como sistema, quero consumir os eventos de lançamento publicados pelo serviço de Lançamentos e atualizar o saldo consolidado do dia correspondente, para manter o relatório de saldo diário sempre disponível e atualizado.

Critérios de aceite:
- Dado um evento de lançamento consumido, quando processado, então o saldo do dia correspondente é atualizado (soma de créditos e débitos) refletindo o novo lançamento.
- Dado o mesmo evento entregue mais de uma vez (reentrega do broker, garantia at-least-once), quando processado novamente, então o saldo final não é duplicado — o consumer é idempotente.
- Dado um evento cujo processamento falha após as tentativas de retry configuradas, quando isso ocorre, então a mensagem é encaminhada a uma dead-letter queue (DLQ) e não bloqueia o processamento das mensagens seguintes.

**RF04 — Consultar saldo consolidado diário**

Como comerciante, quero consultar o relatório de saldo diário consolidado, para saber minha posição de caixa em uma data ou período.

Critérios de aceite:
- Dado que existem lançamentos processados para uma data, quando o saldo consolidado dessa data é consultado, então o valor retornado é a soma (créditos − débitos) de todos os lançamentos daquele dia.
- Dado um período (data inicial e final), quando consultado, então o retorno é o saldo consolidado dia a dia dentro do período.
- Dado que o evento mais recente ainda não foi processado (consistência eventual — ver NFR03), quando o saldo é consultado, então o serviço responde com o dado mais recente disponível, sem erro; a defasagem é um trade-off documentado, não uma falha.

## Requisitos não funcionais

**NFR01 — Isolamento de disponibilidade entre os serviços** *(literal do desafio)*

Descrição: o serviço de Lançamentos não deve ficar indisponível caso o serviço de Consolidado Diário caia.

Critério de aceite mensurável: a comunicação entre os serviços é exclusivamente assíncrona via evento — nunca há chamada síncrona bloqueante do Lançamentos para o Consolidado. Com o Consolidado fora do ar, o Lançamentos mantém 100% de taxa de sucesso ao registrar lançamentos, validado por teste de resiliência (issue #19). Sustentado pelas decisões de outbox transacional (ADR-002) e integração via evento (ADR-001).

**NFR02 — Capacidade de carga do Consolidado Diário** *(literal do desafio)*

Descrição: em dias de pico, o serviço de Consolidado Diário recebe 50 requisições por segundo, com no máximo 5% de perda de requisições.

Critério de aceite mensurável: sob carga sustentada de 50 req/s na API de consulta do Consolidado, a taxa de erro/perda de requisições é ≤ 5%, validado por teste de carga (issue #20). Escopo: "perda de requisições" refere-se a falhas de resposta (erro ou timeout) na API de consulta; perda de eventos na fila de consumo é tratada à parte por retry + DLQ (RF03).

**NFR03 — Consistência eventual entre Lançamentos e Consolidado**

Decorre da integração assíncrona via evento (ADR-001): o saldo consolidado pode apresentar uma defasagem de segundos em relação ao lançamento mais recente. Trade-off deliberado em troca do isolamento de disponibilidade (NFR01). Critério de aceite: a defasagem observada em teste de integração ponta a ponta (issue #18) é medida e permanece na ordem de segundos sob carga normal, não minutos.

**NFR04 — Idempotência no consumo de eventos**

Decorre da garantia at-least-once do broker (ADR-001/ADR-002): reentrega de mensagens é esperada, não excepcional. Critério de aceite: reprocessar o mesmo evento não altera o resultado final do saldo consolidado, validado por teste do consumer (issue #12).

**NFR05 — Observabilidade ponta a ponta**

OpenTelemetry adotado desde o início nos dois serviços (vendor-neutral), permitindo rastrear o fluxo lançamento → outbox → evento → consolidado. Critério de aceite: traces e métricas visíveis localmente (console/Jaeger) e documentados (issue #21).

**NFR06 — Portabilidade de infraestrutura**

Interfaces + Factory na borda de infraestrutura (mensageria, cache) permitem trocar a implementação local (RabbitMQ/Redis/PostgreSQL via Docker Compose) por serviços gerenciados no Azure sem reescrever lógica de domínio (ADR-005). Critério de aceite: a troca de implementação fica restrita a configuração/Factory, sem alteração de código de domínio.

## Requisitos de negócio (origem: desafio)

Transcrição organizada da seção correspondente do desafio original, com rastreabilidade para as issues do backlog que atendem cada item.

### Descritivo da solução

> "Um comerciante precisa controlar o seu fluxo de caixa diário com os lançamentos (débitos e créditos), também precisa de um relatório que disponibilize o saldo diário consolidado."

### Requisitos de negócio

- Serviço que faça o controle de lançamentos → épico do serviço de Lançamentos: #6, #7, #8, #9, #10
- Serviço do consolidado diário → épico do serviço de Consolidado: #11, #12, #13, #14

### Requisitos obrigatórios

| Requisito (texto do desafio) | Issue(s) que atende |
|---|---|
| Mapeamento de domínios funcionais e capacidades de negócio | #2 |
| Refinamento do levantamento de requisitos funcionais e não funcionais | #1 (este documento) |
| Desenho da solução completo (Arquitetura Alvo) | #3, #5 |
| Justificativa na decisão/escolha de ferramentas/tecnologias e de tipo de arquitetura | #4 (ADRs) |
| Pode ser feito na linguagem que você domina | decisão registrada em `CLAUDE.md` (.NET/C#) — sem issue dedicada |
| Testes | #18, #19, #20, além dos testes unitários de cada issue de implementação |
| Readme com instruções claras de como a aplicação funciona e como rodar localmente | #22, #23 |
| Hospedar em repositório público (GitHub) | já atendido — repositório publicado |
| Todas as documentações de projeto devem estar no repositório | #23, #24 |

### Requisitos diferenciais

| Requisito (texto do desafio) | Issue(s) que atende |
|---|---|
| Desenho da solução da Arquitetura de Transição (se necessária) | #26 |
| Estimativa de custos com infraestrutura e licenças | #25 |
| Monitoramento e Observabilidade | #21 |
| Critérios de segurança para consumo (integração) de serviços | #17, #27 |

### Requisitos não funcionais (texto literal do desafio)

> "O serviço de controle de lançamento não deve ficar indisponível se o sistema de consolidado diário cair. Em dias de picos, o serviço de consolidado diário recebe 50 requisições por segundo, com no máximo 5% de perda de requisições."

Detalhados como NFR01 e NFR02 na seção "Requisitos não funcionais" acima.

### Observação do desafio

> "Leve em consideração todos os critérios técnicos mencionados, mas não se prenda somente a eles. Use o teste para demonstrar sua habilidade em tomar decisões sobre o que é importante durante a definição de soluções para o problema de negócio."
