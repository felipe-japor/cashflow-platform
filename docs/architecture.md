# Arquitetura

> Status: consolidado (issue #3). Desenho da solução completo, decorrente das decisões já fechadas em `CLAUDE.md` (seção "Decisões-chave") e do mapeamento de domínios (`domain-mapping.md`, issue #2). Justificativa detalhada de cada decisão tecnológica está nos ADRs (`docs/adr/`, issue #4).

## Visão geral

A solução é composta por **dois serviços**, um por bounded context (ver `docs/domain-mapping.md`):

- **Lançamentos** (`Transaction`) — write model e fonte da verdade do fluxo de caixa. Domínio upstream: registra e valida lançamentos de débito/crédito (RF01) e permite consultá-los (RF02).
- **Consolidado Diário** (`DailyConsolidation`) — read model. Domínio downstream: mantém o saldo consolidado por dia, atualizado incrementalmente a partir dos eventos publicados pelo Lançamentos (RF03), e expõe a consulta do saldo por data/período (RF04).

A integração entre os dois serviços é **exclusivamente assíncrona via evento** (`LancamentoRegistrado`) — nunca há chamada síncrona bloqueante de um serviço para o outro, em nenhuma direção. Este é o padrão arquitetural central da solução, e decorre diretamente de dois requisitos não funcionais do desafio:

- **NFR01** (isolamento de disponibilidade): o serviço de Lançamentos não pode ficar indisponível caso o Consolidado caia. Uma chamada síncrona do Lançamentos para o Consolidado (por exemplo, para "avisar" o consolidado a cada novo lançamento) acoplaria a disponibilidade dos dois serviços — exatamente o que o requisito proíbe. Com integração via evento, o Consolidado fora do ar não impede o Lançamentos de registrar e persistir normalmente; os eventos aguardam na outbox/broker até serem consumidos.
- **NFR02** (capacidade de carga do Consolidado): 50 req/s com ≤5% de perda na API de consulta. Isso é uma característica do Consolidado como read model isolado, otimizado para leitura (dado materializado, baixa cardinalidade, acesso indexado por `Data` — ver ADR-003), e não teria relação direta com uma chamada síncrona entre os serviços; mas reforça por que o Consolidado precisa ser um serviço independente, escalável e testável separadamente do Lançamentos.

O contrato de integração entre os dois domínios é o evento de domínio `LancamentoRegistrado`, publicado pelo Lançamentos via **outbox transacional** (atomicidade entre a escrita do lançamento e a publicação do evento — ADR-002) e consumido pelo Consolidado de forma **idempotente** via inbox pattern (NFR04). O detalhe completo das entidades (`Transaction`, `DailyConsolidation`), do payload do evento e do racional de modelagem (corte de dia em UTC, estratégia de chave primária) está em `docs/domain-mapping.md` — não é repetido aqui.

A consequência aceita dessa escolha é **consistência eventual** entre os dois serviços (NFR03): o saldo consolidado pode apresentar defasagem de segundos em relação ao lançamento mais recente. Trade-off deliberado em troca do isolamento de disponibilidade exigido pelo NFR01.

## Arquitetura alvo (Azure)

A arquitetura alvo mapeia cada peça da solução para um serviço gerenciado equivalente no Azure, sem alterar o desenho lógico descrito acima:

| Peça | Tecnologia alvo | Motivo (resumo — detalhe no ADR correspondente) |
|---|---|---|
| Compute | **Azure Container Apps** | Os dois serviços (Lançamentos, Consolidado) são essencialmente *workers* headless — processam requisições de API e consomem eventos de fila, mas não são a razão de ser do sistema um endpoint HTTP síncrono. Azure App Service pressupõe um listener HTTP como cidadão de primeira classe, o que exigiria um endpoint artificial só para satisfazer a plataforma. Container Apps roda contêineres (mesma imagem usada localmente via Docker Compose) sem essa exigência, com scale-to-zero e scaling por fila/HTTP nativos — útil inclusive para o Consolidado absorver picos (NFR02). |
| Banco de dados | **Azure Database for PostgreSQL Flexible Server** (instância única) | Uma única instância gerenciada hospeda os dois bancos lógicos (Lançamentos e Consolidado), com ownership lógico separado entre os serviços — reduz a complexidade operacional dentro do prazo do desafio. Trade-off de contenção de recursos sob carga compartilhada e evolução recomendada (instâncias separadas) documentados em ADR-003. PostgreSQL foi escolhido como motor (local e Azure, sem diferença de dialeto relevante) por não ter custo de licença e por bom encaixe técnico (JSONB para outbox, window functions para agregação por período) — racional completo em ADR-003. |
| Mensageria | **Azure Service Bus** (RabbitMQ localmente, como implementação de referência) | O outbox transacional do Lançamentos (ADR-002) publica no broker; o Consolidado consome de forma idempotente. Escolha de tecnologia de broker e comparação de alternativas em ADR-004. |
| Cache | Não incluído no escopo inicial | PostgreSQL com índice em `Data` atende o SLA de 50 req/s com margem (baixa cardinalidade do consolidado diário — um registro por dia). Cache seria adicionado só com evidência de teste de carga (issue #20) mostrando necessidade real, não construído preventivamente. Ver ADR-003. |
| Observabilidade | **OpenTelemetry** no código dos dois serviços, exportando para **Azure Monitor** em produção | Instrumentação vendor-neutral desde o início (NFR05) — o código de domínio não depende de SDK de vendor específico; a troca de exportador (console/Jaeger local, Azure Monitor em produção) é configuração, não reescrita. Detalhe em `docs/observability.md` (issue #21). |
| Segredos | **Azure Key Vault**, via Key Vault reference no Container Apps | Segredos de produção (connection strings, credenciais de broker) nunca ficam em variável de ambiente solta — são referenciados do Key Vault pela configuração do Container App. Localmente, segredos de desenvolvimento ficam em configuração local (não produtiva), sem equivalente de Key Vault. |

## Implementação de referência local (Docker Compose)

Para desenvolvimento e para a avaliação do desafio, a solução roda **inteiramente local**, com todas as dependências de infraestrutura em componentes OSS via `docker-compose.yml` (issue #6): um único container PostgreSQL (hospedando os dois bancos lógicos, mesmo padrão da arquitetura alvo) e RabbitMQ (broker), além dos dois serviços da aplicação.

A portabilidade entre essa implementação de referência e a arquitetura alvo Azure descrita acima é sustentada por **interfaces + Factory na borda de infraestrutura de mensageria** — o código de domínio depende só da interface (por exemplo, "publicar evento"); qual implementação concreta é usada (RabbitMQ vs. Azure Service Bus) é resolvido por configuração/Factory, não por reescrita de lógica de domínio (NFR06). Cache não está no escopo comprometido inicialmente (ver tabela acima e ADR-003); se vier a ser necessário, a mesma estratégia de interface+Factory se aplicaria. O racional completo da estratégia de portabilidade está em ADR-005 (ainda a ser detalhado — issue #4).

## Diagramas relacionados

Os diagramas abaixo ilustram visualmente o desenho descrito nesta página (ainda não criados fisicamente — issue #5):

- [C4 - Contexto](diagrams/c4-context.png)
- [C4 - Container](diagrams/c4-container.png)
- [Sequência - Registrar Lançamento](diagrams/sequence-registrar-lancamento.png)
