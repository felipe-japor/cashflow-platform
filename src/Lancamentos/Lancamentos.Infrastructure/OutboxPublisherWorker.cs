using Lancamentos.Application.Ports;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Lancamentos.Infrastructure;

/// <summary>
/// Worker que drena a tabela de outbox para o broker (issue #10, ADR-002). A cada iteração,
/// consulta os eventos pendentes (<see cref="OutboxEvent.PublicadoEmUtc"/> nulo e ainda sem
/// falha persistente), publica via <see cref="IEventPublisher"/> e marca o resultado.
/// </summary>
/// <remarks>
/// Retry aqui é deliberadamente só o próprio ciclo de polling: se <c>PublishAsync</c> falhar
/// (broker fora do ar, timeout etc.), loga e segue — o evento continua pendente e é tentado de
/// novo na próxima iteração, naturalmente. Não há backoff exponencial configurável, fila de
/// retry separada nem dependência de biblioteca externa (Polly): nesse volume, o poll periódico
/// já entrega o mesmo resultado com uma peça móvel a menos (issue #10, decisão registrada no PR).
/// Falha persistente (issue #11) é tratada à parte, via <see cref="OutboxEvent.RegistrarFalha"/>.
/// </remarks>
public sealed class OutboxPublisherWorker(
    IServiceScopeFactory scopeFactory,
    IEventPublisher eventPublisher,
    IOptions<OutboxPublisherOptions> options,
    ILogger<OutboxPublisherWorker> logger) : BackgroundService
{
    private readonly OutboxPublisherOptions _options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(_options.IntervaloSegundos));
        do
        {
            await PublicarPendentesAsync(stoppingToken);
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    /// <summary>
    /// Executa um ciclo de publicação (consulta pendentes, publica, marca resultado). Público
    /// para permitir que testes de unidade exercitem um ciclo isolado, sem depender do laço do
    /// <see cref="PeriodicTimer"/> em <see cref="ExecuteAsync"/> — não é chamado por nenhum outro
    /// código de produção além deste.
    /// </summary>
    public async Task PublicarPendentesAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<LancamentosDbContext>();

        var pendentes = await dbContext.OutboxEvents
            .Where(e => e.PublicadoEmUtc == null && e.FalhaPersistenteEmUtc == null)
            .OrderBy(e => e.OcorridoEmUtc)
            .Take(_options.TamanhoLote)
            .ToListAsync(cancellationToken);

        if (pendentes.Count == 0)
        {
            return;
        }

        foreach (var evento in pendentes)
        {
            try
            {
                await eventPublisher.PublishAsync(evento.EventType, evento.Payload, cancellationToken);
                evento.MarcarComoPublicado(DateTime.UtcNow);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                evento.RegistrarFalha(_options.LimiteTentativas, DateTime.UtcNow);
                if (evento.FalhaPersistenteEmUtc is not null)
                {
                    logger.LogError(ex,
                        "Evento de outbox {EventId} ({EventType}) atingiu o limite de {LimiteTentativas} tentativas e foi marcado como falha persistente.",
                        evento.Id, evento.EventType, _options.LimiteTentativas);
                }
                else
                {
                    logger.LogWarning(ex,
                        "Falha ao publicar evento de outbox {EventId} ({EventType}) — tentativa {Tentativa}/{LimiteTentativas}. Será tentado de novo no próximo poll.",
                        evento.Id, evento.EventType, evento.TentativasFalhas, _options.LimiteTentativas);
                }
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
