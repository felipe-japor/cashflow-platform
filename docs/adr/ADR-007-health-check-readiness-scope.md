# ADR-007: Escopo do health check de readiness (sem o broker)

> Status: aceito.

## Contexto

A issue #15 pede liveness/readiness nos dois serviços, "incluindo dependências (banco, broker)". A leitura literal seria incluir tanto PostgreSQL quanto RabbitMQ na checagem de readiness dos dois serviços. Analisando o caminho real de uma requisição HTTP em cada serviço, essa leitura literal cria um problema real:

- **Lançamentos**: `POST /lancamentos` grava no PostgreSQL (`Transaction` + `OutboxEvent`, mesmo commit) e retorna — a publicação de fato no broker acontece no `OutboxPublisherWorker`, um processo de background completamente desacoplado da requisição (ADR-002).
- **Consolidado**: os endpoints de consulta (`GET /consolidado/...`) só leem do read model no PostgreSQL — quem mantém esse read model atualizado a partir do broker é o `EventConsumerWorker`, também background, separado do caminho de leitura.

Nenhum dos dois caminhos HTTP síncronos toca o RabbitMQ. Se `/health/ready` incluísse o broker, e a plataforma de compute (Azure Container Apps, na arquitetura-alvo) usa readiness para decidir se tira uma instância de rotação de tráfego, uma indisponibilidade do RabbitMQ tiraria o **Lançamentos** de circulação — exatamente a indisponibilidade em cascata que o NFR01 e o outbox transacional (ADR-002) existem para evitar. Marcar "not ready" por uma dependência que a requisição HTTP nem usa reintroduziria, por trás, o mesmo acoplamento de disponibilidade que a arquitetura já resolveu na frente.

## Decisão

`/health/ready` verifica apenas o **PostgreSQL**, nos dois serviços — é a única dependência real do caminho síncrono de cada um. O broker fica de fora de ambos. `/health/live` não toca nenhuma dependência externa (só confirma que o processo está de pé).

A indisponibilidade do broker já é absorvida pelo desenho existente, sem precisar de tratamento adicional no health check:
- Lançamentos: eventos continuam acumulando na tabela de outbox; o `OutboxPublisherWorker` resolve quando o broker voltar (retry via ciclo de poll, ADR-002/issue #10).
- Consolidado: o read model fica temporariamente desatualizado, mas o dado já consolidado continua válido para consulta — consistência eventual já é um trade-off aceito e documentado (NFR03), não uma falha.

## Trade-offs considerados

| Alternativa | Por que não foi escolhida |
|---|---|
| Incluir o broker em `/health/ready` (leitura literal da issue #15) | Acopla a disponibilidade da API HTTP a uma dependência que ela não usa no caminho síncrono — contradiz diretamente o NFR01 no caso do Lançamentos. |
| Broker fora do readiness, mas exposto num endpoint de diagnóstico separado (não usado por load balancer nenhum) | Nenhuma issue ou requisito pede visibility de status de broker fora dos health checks; adicionar um terceiro endpoint sem consumidor real seria complexidade sem uso concreto hoje — pode ser adicionado depois se surgir necessidade operacional real. |

## Consequências

- `/health/live` e `/health/ready` nos dois serviços via `Microsoft.Extensions.Diagnostics.HealthChecks` + `AspNetCore.HealthChecks.NpgSql`, sem pacote de RabbitMQ.
- Os healthchecks já existentes no `docker-compose.yml` (`pg_isready`, `rabbitmq-diagnostics ping`) continuam resolvendo um problema diferente — bootstrap de infraestrutura (`depends_on: condition: service_healthy`) — e não mudam com esta decisão.
- Nenhuma alteração de código necessária no `OutboxPublisherWorker`/`EventConsumerWorker`: eles já lidam com indisponibilidade do broker via retry natural (ADR-002, issue #10/#11), independente do que o health check da API reporta.
