using System.Text.Json;
using Consolidado.Application.Ports;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Shared.Contracts;
using DomainTipoLancamento = Consolidado.Domain.TipoLancamento;

namespace Consolidado.Infrastructure;

/// <summary>
/// Host que liga o consumo real de eventos do broker ao read model do Consolidado (issue #12).
/// No startup, chama <see cref="IEventConsumer.StartConsumingAsync"/> com um callback que
/// desserializa o payload em <see cref="LancamentoRegistrado"/> e aplica o delta via
/// <see cref="IDailyConsolidationRepository.AplicarLancamentoAsync"/> (já idempotente, via inbox
/// pattern com constraint única em <c>ProcessedEvent.EventId</c> — docs/domain-mapping.md).
/// </summary>
/// <remarks>
/// O retry curto (issue #11, consume-side) vive aqui, não em <c>RabbitMqEventConsumer</c>: é uma
/// decisão de orquestração de processamento (quantas vezes tentar antes de desistir), não uma
/// preocupação de transporte. Esgotadas as tentativas, a exceção é relançada — quem trata o
/// nack sem requeue e a dead-letter é a infraestrutura de broker (separação de responsabilidades,
/// cada peça só cuida do que é sua).
/// </remarks>
public sealed class EventConsumerWorker(
    IServiceScopeFactory scopeFactory,
    IEventConsumer eventConsumer,
    IOptions<EventConsumerWorkerOptions> options,
    ILogger<EventConsumerWorker> logger) : BackgroundService
{
    private readonly EventConsumerWorkerOptions _options = options.Value;

    /// <summary>
    /// Tenta conectar e registrar o consumo até conseguir (ou até o host parar). Sem esse laço,
    /// uma falha na conexão inicial (broker temporariamente indisponível) derruba o
    /// <see cref="BackgroundService"/> e, com ele, o host inteiro — visto na prática: o
    /// healthcheck do RabbitMQ pode reportar OK antes do listener AMQP aceitar conexões.
    /// </summary>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await eventConsumer.StartConsumingAsync(ProcessarAsync, stoppingToken);
                return;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex,
                    "Falha ao conectar ao broker para consumir eventos — tentando de novo em {IntervaloReconexaoSegundos}s.",
                    _options.IntervaloReconexaoSegundos);
                await Task.Delay(TimeSpan.FromSeconds(_options.IntervaloReconexaoSegundos), stoppingToken);
            }
        }
    }

    /// <summary>
    /// Processa uma única mensagem (desserializa, aplica o delta com retry curto). Público para
    /// permitir que testes de unidade exercitem o callback diretamente com um
    /// <see cref="IDailyConsolidationRepository"/> fake, sem broker real — é exatamente o
    /// callback passado a <see cref="IEventConsumer.StartConsumingAsync"/> em <see cref="ExecuteAsync"/>.
    /// </summary>
    public async Task ProcessarAsync(string eventType, string payload, CancellationToken cancellationToken)
    {
        if (eventType != nameof(LancamentoRegistrado))
        {
            logger.LogWarning("Evento de tipo desconhecido recebido e ignorado: {EventType}", eventType);
            return;
        }

        var evento = JsonSerializer.Deserialize<LancamentoRegistrado>(payload)
            ?? throw new InvalidOperationException($"Payload do evento '{eventType}' não pôde ser desserializado.");

        for (var tentativa = 1; tentativa <= _options.TentativasCurtas; tentativa++)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var repository = scope.ServiceProvider.GetRequiredService<IDailyConsolidationRepository>();
                await repository.AplicarLancamentoAsync(evento.TransactionId, evento.Data, Map(evento.Tipo), evento.Valor, cancellationToken);
                return;
            }
            // Só absorve aqui se ainda houver tentativa seguinte — na última, a exceção segue
            // sem ser capturada por este catch (filtro falso) e propaga para o consumer de
            // infraestrutura, que faz nack sem requeue.
            catch (Exception ex) when (tentativa < _options.TentativasCurtas)
            {
                logger.LogWarning(ex,
                    "Falha transitória ao processar evento {TransactionId} — tentativa {Tentativa}/{TentativasCurtas}.",
                    evento.TransactionId, tentativa, _options.TentativasCurtas);
                await Task.Delay(_options.DelayEntreTentativasMs, cancellationToken);
            }
        }
    }

    private static DomainTipoLancamento Map(Shared.Contracts.TipoLancamento tipo) => tipo switch
    {
        Shared.Contracts.TipoLancamento.Credito => DomainTipoLancamento.Credito,
        Shared.Contracts.TipoLancamento.Debito => DomainTipoLancamento.Debito,
        _ => throw new ArgumentOutOfRangeException(nameof(tipo), tipo, null)
    };
}
