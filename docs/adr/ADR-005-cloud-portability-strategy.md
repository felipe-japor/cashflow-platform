# ADR-005: Estratégia de portabilidade para nuvem (interfaces + Factory)

> Status: aceito.

## Contexto

A arquitetura-alvo documentada (`docs/architecture.md`) mapeia os componentes de infraestrutura da solução para serviços gerenciados do Azure (Container Apps, Service Bus, PostgreSQL Flexible Server). Ao mesmo tempo, um requisito obrigatório do desafio é um README com instruções claras de como rodar a solução localmente — a implementação de referência precisa funcionar 100% local, via Docker Compose, sem depender de conta ou serviço de nuvem para ser avaliada. NFR06 formaliza essa necessidade como portabilidade de infraestrutura: trocar a implementação local por serviços gerenciados no Azure sem reescrever lógica de domínio.

O ponto de infraestrutura onde essa tensão (rodar local vs. mapear para Azure) realmente se manifesta em código é a borda de **mensageria** — é ali que existem duas implementações concretas distintas e necessárias (RabbitMQ local, Azure Service Bus em produção, ADR-004), cada uma com seu próprio SDK/cliente. Cache foi avaliado e explicitamente removido do escopo inicial (ADR-003): não há, hoje, um segundo backend de cache a abstrair.

## Decisão

Interfaces + Factory na borda de infraestrutura de **mensageria**. O código de domínio (handlers de publicação do outbox, consumer do Consolidado) depende apenas de uma interface própria da aplicação (por exemplo, algo como "publicar evento" / "consumir evento"), nunca do SDK concreto de RabbitMQ ou do Azure Service Bus diretamente. Uma Factory, resolvida por configuração (variável de ambiente/appsettings conforme o ambiente), decide qual implementação concreta da interface é instanciada — RabbitMQ ao rodar localmente via Docker Compose, Azure Service Bus ao rodar na arquitetura-alvo.

Observabilidade é tratada à parte, fora desta ADR: OpenTelemetry é vendor-neutral por natureza (NFR05) — o mesmo código de instrumentação funciona local (exportando para console/Jaeger) e no Azure (exportando para Azure Monitor), apenas trocando o exporter configurado. Não precisa da mesma estratégia de interface + Factory porque a portabilidade já vem embutida no próprio padrão (OpenTelemetry já é a abstração).

## Trade-offs considerados

| Alternativa | Por que não foi escolhida |
|---|---|
| Acoplar o código de domínio diretamente ao SDK do Azure Service Bus, sem interface própria | Descartada porque quebraria a capacidade de rodar localmente sem depender de uma conta Azure — o próprio desafio pune um requisito obrigatório atendido de forma incompleta ou inexistente. Rodar localmente é um requisito obrigatório explícito ("README com instruções claras de como rodar localmente"), não uma conveniência de desenvolvimento. |
| Aplicar a mesma estratégia de interface + Factory também para cache | Não há hoje um segundo backend de cache a abstrair: cache está fora do escopo inicial (ADR-003), pois o SLA de 50 req/s (NFR02) é atendido pelo PostgreSQL com índice em `Data`, dada a baixa cardinalidade do consolidado diário. Introduzir a abstração antes de haver duas implementações reais para alternar seria generalização prematura (viola KISS/YAGNI) — se cache vier a ser necessário (evidência de teste de carga, issue #20), a mesma estratégia de interface + Factory se aplicaria naquele momento. |

## Consequências

- A troca de implementação de mensageria (RabbitMQ ↔ Azure Service Bus) fica restrita a configuração/Factory, sem alteração de código de domínio — mesmo padrão já referenciado na ADR-004 para a escolha de broker.
- O código de domínio e de aplicação nunca importa diretamente os pacotes/SDKs de RabbitMQ ou Azure Service Bus fora da camada de infraestrutura onde a Factory resolve a implementação concreta.
- Se cache for adicionado no futuro com evidência real de necessidade, a mesma estratégia (interface + Factory) deve ser aplicada, mantendo consistência com a decisão desta ADR — mas isso não é antecipado nem construído hoje.
- OpenTelemetry continua sendo adotado desde o início nos dois serviços, independentemente desta ADR, por já ser vendor-neutral (NFR05).
