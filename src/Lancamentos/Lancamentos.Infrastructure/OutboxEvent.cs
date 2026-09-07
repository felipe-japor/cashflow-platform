namespace Lancamentos.Infrastructure;

/// <summary>
/// Registro de outbox (ADR-002): linha persistida na mesma transação de banco que o
/// <see cref="Lancamentos.Domain.Transaction"/> que a originou, garantindo atomicidade entre a
/// escrita do lançamento e a publicação do evento — sem depender do broker estar disponível no
/// momento da escrita.
/// </summary>
/// <remarks>
/// Tipo de persistência, não um conceito de domínio ou de aplicação — por isso vive na camada de
/// Infrastructure, não é exposto pela porta <c>ITransactionRepository</c>. Schema definitivo e a
/// gravação atômica completa são escopo da issue #9.
/// </remarks>
public class OutboxEvent
{
    public Guid Id { get; private set; }
    public string EventType { get; private set; }
    public string Payload { get; private set; }
    public DateTime OcorridoEmUtc { get; private set; }
    public DateTime? PublicadoEmUtc { get; private set; }

    /// <summary>
    /// Quantidade de falhas de publicação já registradas para este evento (issue #11,
    /// publish-side). Incrementado a cada tentativa mal sucedida do worker de outbox.
    /// </summary>
    public int TentativasFalhas { get; private set; }

    /// <summary>
    /// Marcado quando <see cref="TentativasFalhas"/> atinge o limite configurado — a partir daí
    /// o evento para de ser consultado pelo worker de outbox (fica "estacionado", visível para
    /// investigação manual), sem travar o processamento dos demais eventos pendentes. Não existe
    /// uma fila DLQ física para o lado de publicação: o evento nunca saiu do banco, então marcar
    /// esta coluna resolve o mesmo problema de "isolar falha persistente" com uma peça móvel a
    /// menos (ver ADR/decisão registrada na issue #11).
    /// </summary>
    public DateTime? FalhaPersistenteEmUtc { get; private set; }

    private OutboxEvent()
    {
        EventType = string.Empty;
        Payload = string.Empty;
    }

    public OutboxEvent(string eventType, string payload)
    {
        Id = Guid.NewGuid();
        EventType = eventType;
        Payload = payload;
        OcorridoEmUtc = DateTime.UtcNow;
    }

    /// <summary>Chamado pelo worker de outbox (issue #10) após publicar com sucesso no broker.</summary>
    public void MarcarComoPublicado(DateTime utcNow) => PublicadoEmUtc = utcNow;

    /// <summary>
    /// Chamado pelo worker de outbox (issue #10) quando <c>IEventPublisher.PublishAsync</c> falha.
    /// O próprio ciclo de polling já é o mecanismo de retry (evento permanece pendente e é
    /// tentado de novo na próxima iteração) — este contador só existe para decidir quando desistir
    /// (issue #11), não para orquestrar as tentativas em si.
    /// </summary>
    public void RegistrarFalha(int limiteTentativas, DateTime utcNow)
    {
        TentativasFalhas++;
        if (TentativasFalhas >= limiteTentativas)
        {
            FalhaPersistenteEmUtc = utcNow;
        }
    }
}
