# ADR-003: Estratégia de banco de dados (motor e estratégia de chave primária)

> Status: aceito.

## Contexto

Os dois serviços (Lançamentos e Consolidado, ver `docs/domain-mapping.md`) precisam de persistência própria — cada serviço com seu próprio banco (database-per-service, decorrente da separação de bounded contexts do ADR-001). Três decisões de modelagem de dado precisam ser fixadas antes da implementação: qual motor de banco usar, qual estratégia de chave primária adotar para as entidades `Transaction` e `DailyConsolidation`, e como representar o "dia" de negócio nessas entidades.

Requisitos relevantes: o serviço de Lançamentos precisa publicar evento com atomicidade em relação à escrita (outbox transacional, ADR-002 — o outbox roda na mesma transação/mesmo banco da tabela de `Transaction`); o Consolidado precisa sustentar 50 req/s com ≤5% de perda (NFR02) numa consulta de agregado por data/período (RF04); e a arquitetura-alvo mapeia para Azure gerenciado sem reescrita de lógica de domínio (NFR06/ADR-005).

## Decisão

### 1. Motor de banco: PostgreSQL

PostgreSQL para os dois serviços. Motivos:

- **Sem custo de licença** — relevante para a estimativa de custo (issue #25) e para não introduzir uma dependência de licenciamento que o desafio não exige.
- **Roda idêntico local e no Azure** — Docker Compose local (implementação de referência, ADR-005) e Azure Database for PostgreSQL Flexible Server na arquitetura-alvo, sem diferença de dialeto/comportamento relevante para este projeto.
- **Bom encaixe técnico com as duas necessidades concretas do domínio**: JSONB para a tabela de outbox (payload do evento `LancamentoRegistrado` como JSON estruturado, sem precisar de coluna por campo) e window functions/agregação para o cálculo de saldo por período (RF04).
- **SQL Server não foi escolhido** porque não há indício de que a empresa entrevistadora o usa como padrão — se soubéssemos disso, seria um argumento real a favor; na ausência dessa informação, introduzir custo de licença sem benefício técnico correspondente violaria KISS.

### 2. Estratégia de chave primária: UUID para `Transaction` e `DailyConsolidation`

As duas entidades usam `UUID` como chave primária (`Id`), gerado na aplicação (ou via `gen_random_uuid()`/`uuid-ossp` no Postgres — detalhe de implementação, não desta ADR).

**Racional correto** (uma versão anterior deste racional, discutida entre Felipe e o Arquiteto Adjunto, continha um argumento equivocado — registrado aqui para não repetir o erro):

- **O que a decisão de UUID como PK *não* significa**: não é "UUID elimina a necessidade de constraint de unicidade em `Data`". Essa constraint continua necessária de qualquer forma — "um registro de consolidado por dia" é regra de negócio real (RF03/RF04), garantida por um **índice único em `Data`**, independente de qual campo é a chave primária.
- **O que a decisão de UUID como PK realmente resolve**: separação entre identidade técnica (`Id`, usado para referenciar o registro dentro do próprio sistema — por exemplo, `Transaction.Id` é o dado carregado no evento `LancamentoRegistrado` para rastreabilidade e idempotência, ver `docs/domain-mapping.md`) e regra de negócio (`Data` como chave de negócio, com sua própria constraint de unicidade). É uma questão de consistência de modelagem entre os dois agregados, não de necessidade técnica de deduplicação — essa já é resolvida pelo índice único em `Data`.
- **Sobre o custo de UUID como PK em PostgreSQL**: o receio clássico de "UUID como PK causa fragmentação de índice/sequential scan" vem de bancos com **índice clusterizado** (SQL Server, MySQL/InnoDB), onde a PK define a ordem física de armazenamento das linhas na tabela, e UUIDs (não sequenciais) fragmentam essa ordem a cada insert. **PostgreSQL não tem índice clusterizado por padrão** — tabelas são armazenadas como heap, e a PK é apenas mais um índice B-tree entre outros, sem definir ordem física das linhas (a menos que se rode `CLUSTER` explicitamente, o que não é prática de produção aqui). Esse receio, portanto, não se aplica com a mesma força em Postgres.
- Com o índice único secundário em `Data`, a consulta mais comum do domínio (RF04 — saldo por data ou por período) é resolvida via esse índice (index-only scan quando possível), independentemente de a PK ser `Data` ou `UUID`. O custo extra de usar `UUID` como PK em vez de `Data` é desprezível neste desenho.

### 3. Corte de dia em UTC + tipo `DateOnly`

O "dia" de negócio (`Transaction.Data` e `DailyConsolidation.Data`) é definido em **UTC** e modelado como `DateOnly` (sem componente de hora/fuso) nas duas entidades — mesma decisão registrada em `docs/domain-mapping.md`, documentada aqui também por ser, em essência, uma decisão de modelagem de dado/schema.

Motivo: nenhum requisito do desafio menciona comerciante multi-fuso ou fuso configurável; modelar suporte a isso agora seria generalizar para um caso hipotético (viola KISS). A conversão de timestamp real para "dia de negócio" acontece uma única vez, na borda (registro do lançamento / publicação e consumo do evento) — a partir daí, a coluna `Data` nas duas tabelas é `date` (Postgres), sem ambiguidade de hora ou fuso carregada pelas camadas seguintes.

### 4. Instância compartilhada de PostgreSQL (ownership lógico separado)

Na implementação proposta, os serviços de Lançamentos e Consolidado possuem ownership lógico separado sobre seus dados, porém utilizam a mesma instância de PostgreSQL para reduzir a complexidade operacional da solução. A comunicação assíncrona elimina a dependência síncrona entre os serviços, garantindo que uma indisponibilidade da aplicação de Consolidação não impeça o registro de novos lançamentos. Entretanto, o compartilhamento da mesma infraestrutura de banco de dados representa um ponto de contenção comum. Sob carga elevada no serviço de Consolidação, recursos compartilhados como CPU, I/O e conexões podem afetar indiretamente o serviço de Lançamentos. Em um cenário de produção com requisitos mais rígidos de isolamento e disponibilidade, a evolução recomendada seria utilizar bancos ou instâncias de banco independentes para cada serviço.

Essa evolução (bancos/instâncias independentes por serviço) é candidata natural da arquitetura de transição tratada na issue #26 (diferencial) — não antecipada aqui.

### 5. Sem cache, réplica de leitura ou particionamento no escopo inicial

O requisito de carga do Consolidado (NFR02 — 50 req/s, ≤5% de perda) não justifica, de antemão, cache, réplica de leitura ou particionamento de tabela: a projeção de consolidado diário tem baixa cardinalidade (poucas linhas, um registro por dia) e é acessada via índice único em `Data` (seção 2 desta ADR) — PostgreSQL sozinho atende essa carga com margem ampla. Introduzir essas estratégias agora, sem evidência de que são necessárias, seria otimização prematura (viola KISS/YAGNI).

Essas estratégias só serão consideradas mediante evidência real obtida no teste de carga (issue #20) — não construídas preventivamente.

## Trade-offs considerados

| Alternativa | Por que não foi escolhida |
|---|---|
| SQL Server como motor | Custo de licença sem benefício técnico correspondente; não há indício de que a empresa entrevistadora o usa como padrão. |
| `Data` como chave primária de `DailyConsolidation` (em vez de `UUID` + índice único) | Funcionaria tecnicamente, mas mistura identidade técnica com regra de negócio e quebra a consistência de padrão com `Transaction` (que precisa de `Id` técnico próprio para ser referenciada pelo evento). Sem ganho de performance relevante em Postgres (heap, não índice clusterizado) para compensar essa inconsistência. |
| Chave sequencial (`bigserial`/`identity`) como PK | Evitada para não acoplar a geração de identidade ao banco (dificulta gerar o `Id` na aplicação antes de persistir, o que é útil para o outbox — o evento já nasce com o `TransactionId` definido antes do commit). Em Postgres o argumento de performance de índice sequencial vs. UUID é marginal pelos motivos já expostos. |
| `DateTime`/`DateTimeOffset` para `Data`, com fuso configurável por comerciante | Generalização prematura — nenhum requisito do desafio pede isso; adicionaria complexidade (conversões, ambiguidade de horário de verão, etc.) sem problema real correspondente. |

## Consequências

- Cada serviço mantém seu próprio banco lógico PostgreSQL, com ownership lógico separado e sem acesso cruzado a tabelas do outro serviço — mas ambos os bancos lógicos vivem na mesma instância de PostgreSQL (seção 4), o que introduz um ponto de contenção de recursos compartilhados não presente num cenário de instâncias totalmente isoladas.
- A tabela de outbox (ADR-002) vive no mesmo banco/transação da tabela `Transaction`, usando JSONB para o payload do evento.
- `DailyConsolidation` tem `Id UUID` como PK e índice único em `Data`; toda leitura por data/período (RF04) usa esse índice, não a PK.
- Se, no futuro, houver necessidade real de suporte a múltiplos fusos horários por comerciante, será uma mudança de escopo tratada como tal (nova ADR ou revisão desta), não uma extensão do tipo `DateOnly` hoje.
- Infraestrutura local (Docker Compose) e arquitetura-alvo (Azure Database for PostgreSQL Flexible Server) usam o mesmo motor, sustentando a portabilidade descrita em ADR-005/NFR06.
