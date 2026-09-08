# Estimativa de Custos

> Status: preenchido (issue #25, diferencial). Documento puro — nenhum deploy real foi feito para produzi-lo. Estrutura e diretriz de conteúdo fechadas com Felipe, o Arquiteto Adjunto e o infraAgent em 2026-09-08 (ver issue #25).

**Preços de referência: 08/09/2026.** Preço de nuvem muda com frequência — reconfira antes de qualquer decisão de orçamento. Câmbio usado (USD → BRL): **R$ 5,12/US$** (Investing.com, fechamento de 08/09/2026) — cobranças em USD (Vultr, Azure) são faturadas na moeda original, sujeitas a câmbio do dia e, na Azure, possível IOF.

Dois níveis de infraestrutura: o atual/inicial (Nível 1) e o alvo já documentado em `docs/architecture.md` (Nível 2). Cada nível repete a mesma tabela de 5 colunas usada lá (Compute | Banco | Mensageria | Observabilidade | Segredos).

## Nível 1 — 2 nós, topologia assimétrica (atual/inicial)

Duas VPS, não uma — para que NFR01 (Lançamentos não pode ficar indisponível se Consolidado cair) valha também na infraestrutura, não só no desenho lógico. Posicionamento assimétrico, não uma separação simétrica:

- **Nó A** (~4GB RAM, 2 vCPU): `lancamentos-api` + `postgres` + `rabbitmq`
- **Nó B** (~1GB RAM, 1 vCPU): `consolidado-api` sozinho

Se o Nó B cair — processo ou host inteiro —, o Nó A não é afetado. O inverso (Nó A cair afeta os dois) continua existindo, mas é o mesmo risco de contenção de recursos compartilhados que o ADR-003 (seção 4) já registra e aceita conscientemente — não é uma lacuna nova. As connection strings de Postgres/RabbitMQ já são parametrizadas via variável de ambiente nos dois serviços (`docker-compose.yml`, `appsettings.json`), então essa topologia é puramente deploy — nenhuma mudança de código.

| Peça | Tecnologia | Custo estimado |
|---|---|---|
| Compute (Nó A) | VPS ~4GB RAM/2 vCPU — Lançamentos + Postgres + RabbitMQ (ver comparação abaixo) | R$ 30,39 a R$ 110,00/mês, variando por fornecedor |
| Compute (Nó B) | VPS pequena (~1GB RAM/1 vCPU) — só Consolidado | US$ 5,00/mês ≈ R$ 25,60/mês (Vultr `vc2-1c-1gb`, único fornecedor com esse tier cotado publicamente) |
| Banco | PostgreSQL containerizado no Nó A (`postgres:16-alpine`, ADR-003) | Incluído no Compute (Nó A) |
| Mensageria | RabbitMQ containerizado no Nó A (ADR-004) | Incluído no Compute (Nó A) |
| Observabilidade | OpenTelemetry → console/Aspire Dashboard local (NFR05) | Incluído no Compute |
| Segredos | Variáveis de ambiente / `.env` — sem Key Vault neste nível | Sem custo adicional |

**Total (Vultr, único par de tiers com fonte pública para os dois nós):** R$ 102,40 + R$ 25,60 = **R$ 128,00/mês** — delta de +25% sobre uma VPS única, para fechar NFR01 também na infraestrutura (contra ≈R$ 542,65/mês do Nível 2, que resolve a mesma lacuna).

Ressalva: KingHost, Locaweb e HostGator não têm um tier ~1GB/1 vCPU cotado publicamente para o Nó B — só a Vultr tem as duas pontas sourced. Rede entre os nós expõe Postgres/RabbitMQ (hoje só em `localhost` no Compose) — operacionalmente trivial (firewall restrito ao IP do Nó B), mas é superfície nova; revisão de postura de segurança fica a cargo do `secAgent` se necessário.

### Comparação de fornecedores — Nó A (datacenter/suporte no Brasil)

Configuração de referência: ~4GB RAM, 2 vCPUs, disco 70–100GB (folgado para o volume do desafio).

| Fornecedor | Plano | RAM / vCPU / disco | Preço mensal | Fonte |
|---|---|---|---|---|
| Vultr (São Paulo) | `vc2-2c-4gb` | 4GB / 2 vCPU / 80GB SSD | US$ 20,00/mês (≈ R$ 102,40) | api.vultr.com/v2/plans |
| Locaweb Cloud | "Medium" (pay-as-you-go, 2 zonas) | 4GB / 2 vCPU / 80GB | R$ 80,00 (VM) + R$ 30,00 (NAT Gateway obrigatório) = **R$ 110,00/mês** | locaweb.com.br/locaweb-cloud/ |
| KingHost | VPS 4GB | 4GB / 2 vCPU / 70GB SSD | R$ 53,90/mês | king.host/servidor-vps |
| HostGator Brasil | VPS NVMe 4 (OCI, São Paulo) | 4GB DDR5 / 2 vCPU / 100GB NVMe | R$ 30,39/mês — **promocional 1º ciclo (68% off)**; renovação não publicada | hostgator.com.br/servidor-vps |

Fornecedores excluídos:

| Fornecedor | Motivo |
|---|---|
| Contabo | Sem datacenter no Brasil (regiões: UE, RU, EUA, Singapura, Japão, Índia, Austrália). |
| DigitalOcean | Sem região São Paulo/Brasil. |
| UOL Host | Sem produto de VPS/Cloud Server confirmável no site público. |
| Magalu Cloud | Tem datacenter no Brasil, mas preço depende de simulador interativo — não confirmável via página estática. |

### "Redundância barata" — backup/snapshot do Nó A (cotado à parte)

A diretriz da issue #25 pede que isso **não** entre no custo-base — é adicional/opcional. O 2º nó (failover mínimo) que essa seção cotava antes já virou o Nó B do Consolidado acima, com propósito diferente (isolamento de NFR01, não standby); redundância adicional aqui é sobre proteger o Nó A (Postgres/RabbitMQ/Lançamentos) de perda de dados, não de queda de host:

| Fornecedor | Backup/snapshot |
|---|---|
| Vultr | US$ 0,05/GB/mês; backup agendado +20% (≈ +US$ 4,00/mês) |
| Locaweb Cloud | R$ 0,30/GB/mês |
| KingHost | Não publicado |

Um standby adicional para o Nó A (terceiro nó, failover de verdade) não é cotado aqui — seria trabalho de orquestração adicional (promoção do standby, IP/DNS, sincronização de dados), coerente com o risco já registrado em `docs/transition-architecture.md` ("alta disponibilidade de infraestrutura é ortogonal ao que o desafio pede").

### Rastreabilidade a requisitos — Nível 1

**NFR01**: atendido no desenho lógico (integração assíncrona via evento, validada pelo teste de resiliência da issue #19) **e** na infraestrutura — o Nó B (Consolidado) isolado do Nó A (Lançamentos, Postgres, RabbitMQ) garante que uma queda do Consolidado, incluindo queda do host inteiro, não derruba o Lançamentos. O residual é o inverso (queda do Nó A afeta os dois), que é o mesmo risco de contenção de recursos compartilhados já aceito no ADR-003 (seção 4) — não uma lacuna nova.

**NFR02**: o SLA (50 req/s, ≤5% perda) foi validado via NBomber (issue #20) contra `localhost`, sem restrição de recursos nem overhead de rede real — não há evidência de que se sustenta no footprint do Nó A (~4GB/2 vCPU). Risco conhecido, com gatilho: medir contra a VPS real antes de assumir conformidade em produção.

**NFR06 (portabilidade)**: já satisfeita por design (`IEventPublisher`/`IEventConsumer` + DI, ADR-005), independente do nível de infraestrutura.

## Nível 2 — Arquitetura-alvo (já documentada em `docs/architecture.md`)

Container Apps + Flexible Server + Azure Service Bus (tier **Basic** — sem fan-out, único produtor/consumidor, ADR-004) + Key Vault + Azure Monitor. Preços via Azure Retail Prices API (`prices.azure.com/api/retail/prices`, `armRegionName eq 'brazilsouth'`).

| Peça | Tecnologia | Custo estimado |
|---|---|---|
| Compute | Azure Container Apps (consumption), 2 apps: `lancamentos-api`, `consolidado-api` | ≈ US$ 73,44/mês ≈ **R$ 376,05/mês** (cálculo abaixo) |
| Banco | Postgres Flexible Server, Burstable **B1ms** (1 vCore/2GiB) | US$ 0,035/h + US$ 0,2185/GiB/mês (32GiB min.) ≈ **R$ 166,60/mês** |
| Mensageria | Azure Service Bus, tier **Basic** | US$ 0,05/milhão de operações — Basic não tem faixa gratuita (isso é benefício do tier Standard, que tem cobrança-base própria). Cobrado desde a 1ª operação, mas irrelevante no volume deste projeto (1 evento/lançamento registrado): mesmo a 100 mil operações/mês, custo ≈ US$ 0,005/mês → **≈ R$ 0/mês** |
| Observabilidade | OpenTelemetry → Azure Monitor | 5GB/mês grátis, depois US$ 4,60/GiB — volume esperado dentro da faixa gratuita |
| Segredos | Azure Key Vault (Standard) | US$ 0,03/10.000 operações — desprezível para o volume esperado |

**Cálculo do compute** (0,5 vCPU / 1GiB por app, réplica mínima 1, 730h/mês, 2 apps):

- vCPU: 2.628.000 vCPU-s − 180.000 grátis = 2.448.000 faturáveis × US$ 0,000024 = **US$ 58,75**
- Memória: 5.256.000 GiB-s − 360.000 grátis = 4.896.000 faturáveis × US$ 0,000003 = **US$ 14,69**
- Total: **US$ 73,44/mês** (requisições não incluídas — dependem do tráfego real; 50 req/s é o pico do NFR02, não a média)

**Alternativa B2s para o banco**: ≈ R$ 559,05/mês — mais de 3x o B1ms, sem escala linear de vCPU/RAM. Como o SLA já é atendido pelo Postgres simples com índice em `Data` (ADR-003), **B1ms é o ponto de partida recomendado**.

**Total aproximado (Compute + Banco, B1ms):** ≈ **R$ 542,65/mês**, mais os valores residuais de Mensageria/Observabilidade/Segredos.

### Nota — banco+mensageria totalmente self-hosted (não cotado a sério)

Alternativa mais barata de demo/dev: Postgres **e** RabbitMQ containerizados dentro do Container Apps Environment, evitando o custo de Flexible Server e Service Bus. Não precificada — a diretriz da issue #25 é explícita que não é recomendação séria para produção:

- Sem HA/backup automáticos que o Flexible Server oferece nativamente.
- Storage via Azure Files (performance/latência inferior a managed disk) — inadequado para banco transacional real.
- Mensageria containerizada é cobrada por segundo de vCPU/memória (sempre ativa) — sai mais cara que o Service Bus Basic, cujo custo por operação é desprezível no volume deste projeto, mesmo sem faixa gratuita no tier.

### Rastreabilidade a requisitos — Nível 2

**NFR01**: coberto também na infraestrutura — Container Apps escala e reinicia os dois serviços independentemente, sem depender de uma topologia manual de nós para isolar a falha (como no Nível 1).

**NFR02**: mesmo teste (issue #20) ainda roda contra `localhost` — a mesma ressalva do Nível 1 se aplica, agora contra Container Apps/Flexible Server reais.

**NFR06**: trocar RabbitMQ por Service Bus é configuração de DI (ADR-004/ADR-005), sem tocar lógica de domínio.

## Resumo comparativo

| Nível | Compute + Banco (aprox., B1ms) | Observação principal |
|---|---|---|
| 1 — 2 nós (Nó A + Nó B) | R$ 56,00 a R$ 135,60/mês (Nó A por fornecedor + Nó B ≈ R$ 25,60) | Mais barato, NFR01 resolvido também na infra; resta SPOF do Nó A (Postgres+RabbitMQ+Lançamentos, risco já aceito no ADR-003) e NFR02 não validado neste footprint |
| 2 — Arquitetura-alvo | ≈ R$ 542,65/mês | Sem SPOF residual — cada peça (compute, banco, mensageria) escala/recupera de forma independente e gerenciada |

Valores do Nível 2 assumem 0,5 vCPU/1GiB por serviço, réplica mínima 1, e tráfego dentro das faixas gratuitas — não são cotação oficial da calculadora Azure com topologia final; reconferir com dimensionamento real antes de decisão de orçamento formal.
