# ADR-004: Escolha de tecnologia de mensageria (broker)

> Status: aceito.

## Contexto

A ADR-001 decide que a integração entre Lançamentos e Consolidado é via evento assíncrono, publicado por uma outbox transacional (ADR-002). Isso exige um broker de mensageria concreto entre os dois serviços. É preciso escolher qual tecnologia usar tanto na implementação de referência local (requisito obrigatório do desafio: README com instruções claras de rodar localmente) quanto na arquitetura-alvo Azure.

O volume de carga real conhecido é o de NFR02: 50 requisições por segundo em dias de pico no lado de consulta do Consolidado — carga trivial para praticamente qualquer broker de mensageria estabelecido. Não há, no desafio, nenhum requisito de multi-partição, replay de longo prazo, múltiplos grupos de consumidores independentes sobre o mesmo tópico, ou throughput de ordens de grandeza maiores que o de NFR02.

## Decisão

**RabbitMQ** na implementação de referência local (via Docker Compose, junto de PostgreSQL — ADR-003), e **Azure Service Bus** na arquitetura-alvo em produção. A troca entre as duas implementações é resolvida pela mesma abstração de interface + Factory descrita na ADR-005 — o código de domínio depende apenas da interface de publicação/consumo de evento, nunca do SDK concreto do broker.

RabbitMQ foi escolhido para a implementação local por ser simples de self-host via Docker Compose, uma ferramenta amplamente compreendida no mercado, e por suportar nativamente o padrão pub/sub (exchange/queue) necessário para a relação um-produtor/um-consumidor entre Lançamentos e Consolidado — suficiente para o volume de NFR02 com folga.

## Trade-offs considerados (RabbitMQ vs. Kafka vs. Azure Service Bus)

| Alternativa | Por que não foi escolhida |
|---|---|
| Kafka | Descartado para este projeto: 50 req/s (NFR02) é uma carga trivial para qualquer um dos brokers considerados, e o Kafka adicionaria complexidade operacional real (gestão de partições, cluster, offsets, replicação) sem nenhum ganho correspondente nessa escala. Escolher a ferramenta adequada ao volume real do problema é sinal de maturidade arquitetural — usar "a ferramenta mais robusta que existe" por padrão, independentemente da escala real, vai contra KISS. |
| Azure Service Bus como implementação também local (via emulador ou conta de nuvem) | Rejeitada para a implementação de referência local pelo mesmo motivo do ADR-005: acoplar a experiência de "rodar localmente" (requisito obrigatório do desafio) a uma dependência de nuvem quebraria a promessa de rodar 100% via Docker Compose sem conta Azure. Azure Service Bus continua sendo a escolha correta para a arquitetura-alvo em produção, via a mesma interface. |

## Consequências

- A troca de RabbitMQ (local) para Azure Service Bus (produção) é uma troca de configuração/Factory (ADR-005), não uma reescrita de lógica de domínio ou dos handlers de publicação/consumo de evento.
- O Docker Compose da implementação de referência inclui um container RabbitMQ, ao lado do PostgreSQL (ADR-003).
- Se, no futuro, o volume real de eventos crescer ordens de grandeza além do previsto em NFR02 (por exemplo, múltiplos consumidores independentes do mesmo evento, necessidade de replay de longo prazo), a adequação de RabbitMQ/Azure Service Bus deve ser reavaliada nessa ocasião, como mudança de escopo real — não antecipada aqui.
