# Monitoramento e Observabilidade

> Status: implementado (issue #21, diferencial). Instrumentação com OpenTelemetry nos dois
> serviços (`Lancamentos.Api`, `Consolidado.Api`), export único via OTLP, visualização local via
> Aspire Dashboard.

## Estratégia de instrumentação (OpenTelemetry)

OpenTelemetry foi decidido desde o planejamento inicial (ver `CLAUDE.md` — Decisões-chave) por
ser vendor-neutral: o mesmo código de instrumentação funciona local (Aspire Dashboard, via Docker
Compose) e na arquitetura-alvo Azure (Azure Monitor/Application Insights, via o mesmo protocolo
OTLP) — troca de backend é configuração (`OTEL_EXPORTER_OTLP_ENDPOINT`), não reescrita de código.

Os três sinais (traces, métricas, logs) são registrados via `AddOpenTelemetry()` no composition
root de cada serviço (`AddLancamentosInfrastructure`/`AddConsolidadoInfrastructure`, em
`src/*/Infrastructure/DependencyInjection.cs`) — mesmo lugar onde as demais dependências de
infraestrutura já são registradas, sem introduzir uma camada nova só para isto.

### Baseline (auto-instrumentação, os dois serviços)

| Sinal | Fonte |
|---|---|
| Logs | `ILogger` estruturado já usado no código de produção, exportado via `.WithLogging()` — sem pacote extra além do exporter OTLP. |
| Traces HTTP | `OpenTelemetry.Instrumentation.AspNetCore` — os dois serviços são hosts ASP.NET Core reais (endpoints minimal API). |
| Métricas de runtime | `OpenTelemetry.Instrumentation.Runtime` — GC, thread pool, alocação. |
| Traces/métricas de banco | `Npgsql.OpenTelemetry` (GA) — queries via Npgsql, cobre o mesmo propósito do pacote oficial de EF Core, que segue em beta há anos sem sinal de GA. |

`OpenTelemetry.Instrumentation.Http` **não** foi adicionado: nenhum dos dois serviços faz
chamada HTTP de saída hoje (a integração entre eles é 100% assíncrona via evento — ADR-001), não
há alvo para essa instrumentação.

### Instrumentação manual do RabbitMQ

Não existe pacote de auto-instrumentação para `RabbitMQ.Client`. Sem instrumentação manual, cada
serviço geraria uma trace desconexa do outro, o que não evidenciaria a arquitetura orientada a
eventos que é o centro deste desafio (ADR-001/ADR-002). A instrumentação cobre a correlação
publish↔consume entre os dois serviços:

- **Publish** (`Lancamentos.Infrastructure.RabbitMqEventPublisher.PublishAsync`): abre um
  `Activity` de kind `Producer` a partir de um `ActivitySource` próprio
  (`Lancamentos.Infrastructure.Telemetry.ActivitySource`) e injeta o trace context (W3C
  `traceparent`) nos headers AMQP da mensagem via `Propagators.DefaultTextMapPropagator.Inject(...)`,
  antes do `BasicPublishAsync`.
- **Consume** (`Consolidado.Infrastructure.RabbitMqEventConsumer.StartConsumingAsync`): extrai o
  trace context dos headers AMQP recebidos via `Propagators.DefaultTextMapPropagator.Extract(...)`
  e abre um `Activity` de kind `Consumer` conectado a esse contexto
  (`Consolidado.Infrastructure.Telemetry.ActivitySource`).

Resultado: o trace do consumo em Consolidado aparece corretamente como filho do trace da
publicação em Lançamentos, mesmo os dois processos rodando em serviços/containers separados.

**Lacuna documentada — fora de escopo desta issue**: a trace **não é reconectada até o request
HTTP original** (`POST /lancamentos`) que deu origem ao lançamento. O outbox (ADR-002) desacopla
deliberadamente a escrita HTTP do processamento assíncrono — o request já respondeu `201` ao
cliente antes do `OutboxPublisherWorker` publicar o evento, então não há uma `Activity` HTTP viva
no momento do publish para servir de pai. Reconectar exigiria persistir o `traceparent` do
request original numa coluna nova em `OutboxEvent` e usar `ActivityLink` para linkar (sem
sobrescrever) o trace do publish ao trace do request — custo desproporcional ao valor para este
diferencial. Fica registrado como melhoria futura, não implementada.

### Métricas de negócio

Dois `Histogram<double>` (em milissegundos), um em cada serviço, evidenciam com dado real a
consistência eventual já descrita em texto no ADR-001:

| Métrica | Onde é medida | O que mede |
|---|---|---|
| `lancamentos.outbox.lag_ms` | `OutboxPublisherWorker.PublicarPendentesAsync`, logo após um publish bem-sucedido | Tempo entre o lançamento ocorrer (`OutboxEvent.OcorridoEmUtc`) e o worker conseguir publicar o evento no broker. |
| `consolidado.consolidacao.lag_ms` | `RabbitMqEventConsumer`, logo após o callback de consumo concluir com sucesso | Tempo entre a mensagem ser publicada no broker e o consolidado diário ser efetivamente atualizado. |

**Detalhe de implementação**: o "momento da publicação" usado pela segunda métrica é o timestamp
AMQP padrão (`BasicProperties.Timestamp`), atribuído pelo `RabbitMqEventPublisher` ao publicar
(o mesmo objeto `BasicProperties` já precisava existir para carregar os headers do trace
context, então atribuir o timestamp ali é custo marginal zero). As duas métricas são
complementares, não sobrepostas: juntas cobrem o intervalo completo lançamento ocorrido → evento
publicado → consolidado atualizado, sem precisar propagar `OcorridoEmUtc` do Lançamentos para o
Consolidado (que exigiria um campo novo no contrato de evento `LancamentoRegistrado`, fora do
escopo desta issue). Mensagens sem timestamp (não deveria acontecer em produção — o publisher
sempre atribui) não são contabilizadas, para não poluir o histograma com um valor absurdo.

## Como visualizar localmente (Aspire Dashboard)

O `docker-compose.yml` sobe um container `mcr.microsoft.com/dotnet/aspire-dashboard` como backend
OTLP local, no lugar do Jaeger considerado inicialmente: o exportador nativo do Jaeger foi
descontinuado (hoje só recebe via OTLP também) e Jaeger sozinho não faz métricas — precisaria de
Prometheus/Grafana à parte. O Aspire Dashboard junta traces, métricas e logs num único container,
sem custo adicional de peças móveis.

1. `docker compose up -d --build` (ver `README.md` para o fluxo completo).
2. Abrir `http://localhost:18888` no navegador (auth desabilitada — `DASHBOARD__FRONTEND__AUTHMODE=Unsecured`
   e `DASHBOARD__OTLP__AUTHMODE=Unsecured`, ambiente **local apenas**, sem exposição externa).
3. Registrar um lançamento (`POST /lancamentos` em `http://localhost:5101`) e aguardar alguns
   segundos (ciclo do `OutboxPublisherWorker`, default 5s).
4. Na aba **Traces**: buscar pelo recurso `Lancamentos` ou `Consolidado` (nome do serviço,
   definido via `ConfigureResource(r => r.AddService(...))`) — o trace do publish (span
   `Producer`) aparece com o trace do consume (span `Consumer`) como filho, correlacionados pelo
   `traceparent` propagado nos headers AMQP.
5. Na aba **Metrics**: buscar por `lancamentos.outbox.lag_ms` e `consolidado.consolidacao.lag_ms`
   para ver a distribuição real do lag de consistência eventual, e pelas métricas de runtime/ASP.NET
   Core/Npgsql já expostas pela auto-instrumentação.
6. Na aba **Structured Logs**: os logs estruturados (`ILogger`) já emitidos pelo código de
   produção (ex.: falhas de publicação, falha persistente na outbox, retry de consumo).

Cada serviço aponta para o dashboard via a variável de ambiente padrão
`OTEL_EXPORTER_OTLP_ENDPOINT` (lida automaticamente pelo SDK OpenTelemetry, sem código extra) —
mesmo padrão de configuração via ambiente já usado para `RabbitMq__HostName` etc. no
`docker-compose.yml`. Fora do docker-compose (ex.: rodando os serviços localmente via `dotnet run`
apontando para um dashboard já subido), basta exportar a mesma variável apontando para
`http://localhost:18889`.

## Testes

A lógica de injeção/extração do trace context é isolável e testada sem canal/conexão real de
broker:

- `Lancamentos.Infrastructure.Tests.RabbitMqEventPublisherTests` — injeção do `traceparent` nos
  headers a partir de uma `Activity`.
- `Consolidado.Infrastructure.Tests.RabbitMqEventConsumerTests` — extração do `traceparent` de
  headers AMQP (incluindo o formato `byte[]` real com que o RabbitMQ.Client entrega os headers na
  recepção).

A correlação end-to-end (headers efetivamente atravessando o broker entre os dois serviços
containerizados) não tem um teste automatizado dedicado nesta issue — validada manualmente via
`docker compose up` + Aspire Dashboard (passos acima). Automatizá-la ficaria a cargo de estender
`tests/EndToEnd.Tests` para inspecionar spans exportados, o que exigiria um coletor/exporter
adicional só para teste (ex.: `InMemoryExporter` compartilhado entre processos, inviável entre
containers separados) — custo não justificado para este diferencial dentro do orçamento de tempo.
