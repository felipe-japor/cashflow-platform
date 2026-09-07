namespace Consolidado.Infrastructure;

/// <summary>
/// Configuração do worker que consome eventos do broker e atualiza o read model (issue #12).
/// Vinculada à seção "EventConsumerWorker" de configuração (appsettings/variáveis de ambiente).
/// </summary>
public class EventConsumerWorkerOptions
{
    public const string SectionName = "EventConsumerWorker";

    /// <summary>
    /// Tentativas imediatas (com um delay curto entre elas) para absorver falha transitória ao
    /// processar uma mensagem (ex.: contenção momentânea no Postgres) — issue #11, consume-side.
    /// Esgotadas as tentativas, a exceção é relançada: <c>RabbitMqEventConsumer</c> faz nack sem
    /// requeue, e o próprio RabbitMQ roteia para a dead-letter queue via x-dead-letter-exchange.
    /// </summary>
    public int TentativasCurtas { get; set; } = 3;

    public int DelayEntreTentativasMs { get; set; } = 200;

    /// <summary>
    /// Intervalo entre tentativas de conectar ao broker no startup. Sem essa retentativa, uma
    /// indisponibilidade transitória do broker no exato momento em que o serviço sobe (ex.:
    /// RabbitMQ ainda inicializando o listener AMQP mesmo já reportando healthcheck OK — visto
    /// na prática via docker-compose) derruba o host inteiro e ele não se recupera sozinho. Sem
    /// backoff exponencial nem limite de tentativas — mesmo racional do worker de outbox
    /// (issue #10): o próprio ciclo de retentativa já resolve, uma peça móvel a menos.
    /// </summary>
    public int IntervaloReconexaoSegundos { get; set; } = 5;
}
