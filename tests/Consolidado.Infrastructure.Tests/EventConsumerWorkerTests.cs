using Consolidado.Application.Ports;
using Consolidado.Domain;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shared.Contracts;
using DomainTipoLancamento = Consolidado.Domain.TipoLancamento;

namespace Consolidado.Infrastructure.Tests;

/// <summary>
/// Prova a orquestração do consumer (issue #12): desserialização, mapeamento de tipo, e o retry
/// curto para falha transitória com propagação da exceção ao esgotar as tentativas (issue #11,
/// consume-side) — sem broker real, via fake de <see cref="IDailyConsolidationRepository"/>.
/// A idempotência em si (mesmo evento processado duas vezes não duplica o saldo) já está coberta
/// por <see cref="DailyConsolidationRepositoryTests"/>.
/// </summary>
public class EventConsumerWorkerTests
{
    private static EventConsumerWorker CriarWorker(
        IDailyConsolidationRepository repository, EventConsumerWorkerOptions? options = null, IEventConsumer? consumer = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton(repository);
        var scopeFactory = services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();

        return new EventConsumerWorker(
            scopeFactory,
            consumer ?? new EventConsumerFake(),
            Options.Create(options ?? new EventConsumerWorkerOptions { DelayEntreTentativasMs = 1 }),
            NullLogger<EventConsumerWorker>.Instance);
    }

    /// <summary>Fake cujo <see cref="StartConsumingAsync"/> delega a um delegate — permite
    /// simular falha na conexão inicial ao broker (ex.: para testar retry de reconexão).</summary>
    private sealed class EventConsumerFake(Func<Task>? aoIniciar = null) : IEventConsumer
    {
        public Task StartConsumingAsync(Func<string, string, CancellationToken, Task> onMessage, CancellationToken cancellationToken) =>
            aoIniciar?.Invoke() ?? Task.CompletedTask;
    }

    /// <summary>Fake controlado por uma fila de resultados: null aplica com sucesso, uma
    /// exceção enfileirada é lançada naquela chamada.</summary>
    private sealed class DailyConsolidationRepositoryFake : IDailyConsolidationRepository
    {
        private readonly Queue<Exception?> _resultados;
        public List<(Guid EventId, DateOnly Data, DomainTipoLancamento Tipo, decimal Valor)> Aplicados { get; } = [];

        public DailyConsolidationRepositoryFake(params Exception?[] resultados) => _resultados = new Queue<Exception?>(resultados);

        public Task AplicarLancamentoAsync(Guid eventId, DateOnly data, DomainTipoLancamento tipo, decimal valor, CancellationToken cancellationToken)
        {
            var falha = _resultados.Count > 0 ? _resultados.Dequeue() : null;
            if (falha is not null)
            {
                throw falha;
            }

            Aplicados.Add((eventId, data, tipo, valor));
            return Task.CompletedTask;
        }

        public Task<DailyConsolidation?> GetByDataAsync(DateOnly data, CancellationToken cancellationToken) =>
            Task.FromResult<DailyConsolidation?>(null);

        public Task<IReadOnlyList<DailyConsolidation>> GetByPeriodoAsync(DateOnly dataInicial, DateOnly dataFinal, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<DailyConsolidation>>([]);
    }

    [Fact]
    public async Task Aplica_o_delta_com_o_payload_desserializado_e_o_tipo_mapeado()
    {
        var repository = new DailyConsolidationRepositoryFake();
        var worker = CriarWorker(repository);
        var evento = new LancamentoRegistrado(Guid.NewGuid(), new DateOnly(2026, 9, 4), Shared.Contracts.TipoLancamento.Credito, 150.75m);
        var payload = System.Text.Json.JsonSerializer.Serialize(evento);

        await worker.ProcessarAsync(nameof(LancamentoRegistrado), payload, CancellationToken.None);

        var aplicado = Assert.Single(repository.Aplicados);
        Assert.Equal(evento.TransactionId, aplicado.EventId);
        Assert.Equal(evento.Data, aplicado.Data);
        Assert.Equal(evento.Valor, aplicado.Valor);
        Assert.Equal(DomainTipoLancamento.Credito, aplicado.Tipo);
    }

    [Theory]
    [InlineData(Shared.Contracts.TipoLancamento.Credito, DomainTipoLancamento.Credito)]
    [InlineData(Shared.Contracts.TipoLancamento.Debito, DomainTipoLancamento.Debito)]
    public async Task Mapeia_o_tipo_do_contrato_de_evento_para_o_tipo_do_dominio(
        Shared.Contracts.TipoLancamento tipoContrato, DomainTipoLancamento tipoDominioEsperado)
    {
        var repository = new DailyConsolidationRepositoryFake();
        var worker = CriarWorker(repository);
        var evento = new LancamentoRegistrado(Guid.NewGuid(), new DateOnly(2026, 9, 4), tipoContrato, 10m);
        var payload = System.Text.Json.JsonSerializer.Serialize(evento);

        await worker.ProcessarAsync(nameof(LancamentoRegistrado), payload, CancellationToken.None);

        var aplicado = Assert.Single(repository.Aplicados);
        Assert.Equal(tipoDominioEsperado, aplicado.Tipo);
    }

    [Fact]
    public async Task Ignora_eventos_de_tipo_desconhecido_sem_chamar_o_repositorio()
    {
        var repository = new DailyConsolidationRepositoryFake();
        var worker = CriarWorker(repository);

        await worker.ProcessarAsync("EventoDesconhecido", "{}", CancellationToken.None);

        Assert.Empty(repository.Aplicados);
    }

    [Fact]
    public async Task Tenta_novamente_apos_falha_transitoria_e_conclui_com_sucesso_dentro_do_limite()
    {
        var repository = new DailyConsolidationRepositoryFake(new InvalidOperationException("contenção momentânea"), null);
        var worker = CriarWorker(repository, new EventConsumerWorkerOptions { TentativasCurtas = 3, DelayEntreTentativasMs = 1 });
        var evento = new LancamentoRegistrado(Guid.NewGuid(), new DateOnly(2026, 9, 4), Shared.Contracts.TipoLancamento.Credito, 10m);
        var payload = System.Text.Json.JsonSerializer.Serialize(evento);

        await worker.ProcessarAsync(nameof(LancamentoRegistrado), payload, CancellationToken.None);

        Assert.Single(repository.Aplicados);
    }

    [Fact]
    public async Task Relanca_apos_esgotar_as_tentativas_curtas_para_que_o_consumer_faca_nack_sem_requeue()
    {
        var repository = new DailyConsolidationRepositoryFake(
            new InvalidOperationException("falha 1"),
            new InvalidOperationException("falha 2"),
            new InvalidOperationException("falha 3"));
        var worker = CriarWorker(repository, new EventConsumerWorkerOptions { TentativasCurtas = 3, DelayEntreTentativasMs = 1 });
        var evento = new LancamentoRegistrado(Guid.NewGuid(), new DateOnly(2026, 9, 4), Shared.Contracts.TipoLancamento.Credito, 10m);
        var payload = System.Text.Json.JsonSerializer.Serialize(evento);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            worker.ProcessarAsync(nameof(LancamentoRegistrado), payload, CancellationToken.None));

        Assert.Empty(repository.Aplicados);
    }

    [Fact]
    public async Task Tenta_reconectar_ao_broker_ate_conseguir_iniciar_o_consumo()
    {
        // Reproduz o que foi observado na prática via docker-compose: o RabbitMQ pode reportar
        // healthcheck OK antes do listener AMQP aceitar conexões — sem essa retentativa, a
        // primeira falha de conexão derrubaria o host inteiro (BackgroundServiceExceptionBehavior
        // = StopHost) e ele não se recuperaria sozinho.
        var tentativas = 0;
        var consumer = new EventConsumerFake(() =>
        {
            tentativas++;
            return tentativas < 3 ? throw new InvalidOperationException("broker indisponível") : Task.CompletedTask;
        });
        var worker = CriarWorker(
            new DailyConsolidationRepositoryFake(),
            new EventConsumerWorkerOptions { IntervaloReconexaoSegundos = 0 },
            consumer);

        await worker.StartAsync(CancellationToken.None);
        await worker.ExecuteTask!.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(3, tentativas);
    }
}
