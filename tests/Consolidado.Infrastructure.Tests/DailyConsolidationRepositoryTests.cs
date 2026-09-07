using Consolidado.Domain;
using Microsoft.EntityFrameworkCore;

namespace Consolidado.Infrastructure.Tests;

/// <summary>
/// Prova o comportamento atômico real de
/// <see cref="DailyConsolidationRepository.AplicarLancamentoAsync"/>: idempotência via inbox
/// (docs/domain-mapping.md) e o netting incremental no consolidado do dia. Usa EF Core InMemory
/// (sem Postgres) — suficiente para provar a lógica em si; teste de integração com banco real,
/// concorrência e reentrega são escopo das issues #8/#12.
/// </summary>
public class DailyConsolidationRepositoryTests
{
    private static ConsolidadoDbContext CriarDbContext()
    {
        var options = new DbContextOptionsBuilder<ConsolidadoDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new ConsolidadoDbContext(options);
    }

    [Fact]
    public async Task Primeiro_evento_do_dia_cria_o_consolidado_e_aplica_o_delta()
    {
        using var dbContext = CriarDbContext();
        var repository = new DailyConsolidationRepository(dbContext);
        var data = new DateOnly(2026, 9, 4);

        await repository.AplicarLancamentoAsync(Guid.NewGuid(), data, TipoLancamento.Credito, 100m, CancellationToken.None);

        var consolidado = Assert.Single(dbContext.DailyConsolidations);
        Assert.Equal(data, consolidado.Data);
        Assert.Equal(100m, consolidado.Creditos);
        Assert.Equal(100m, consolidado.Saldo);
    }

    [Fact]
    public async Task Marca_o_evento_como_processado_no_inbox_apos_aplicar_o_delta()
    {
        using var dbContext = CriarDbContext();
        var repository = new DailyConsolidationRepository(dbContext);
        var eventId = Guid.NewGuid();

        await repository.AplicarLancamentoAsync(eventId, new DateOnly(2026, 9, 4), TipoLancamento.Credito, 100m, CancellationToken.None);

        var processado = Assert.Single(dbContext.ProcessedEvents);
        Assert.Equal(eventId, processado.EventId);
    }

    [Fact]
    public async Task Reentrega_do_mesmo_evento_nao_aplica_o_delta_duas_vezes()
    {
        using var dbContext = CriarDbContext();
        var repository = new DailyConsolidationRepository(dbContext);
        var eventId = Guid.NewGuid();
        var data = new DateOnly(2026, 9, 4);

        await repository.AplicarLancamentoAsync(eventId, data, TipoLancamento.Credito, 100m, CancellationToken.None);
        await repository.AplicarLancamentoAsync(eventId, data, TipoLancamento.Credito, 100m, CancellationToken.None);

        var consolidado = Assert.Single(dbContext.DailyConsolidations);
        Assert.Equal(100m, consolidado.Creditos);
        Assert.Single(dbContext.ProcessedEvents);
    }

    [Fact]
    public async Task Eventos_diferentes_no_mesmo_dia_acumulam_no_mesmo_consolidado()
    {
        using var dbContext = CriarDbContext();
        var repository = new DailyConsolidationRepository(dbContext);
        var data = new DateOnly(2026, 9, 4);

        await repository.AplicarLancamentoAsync(Guid.NewGuid(), data, TipoLancamento.Credito, 100m, CancellationToken.None);
        await repository.AplicarLancamentoAsync(Guid.NewGuid(), data, TipoLancamento.Debito, 30m, CancellationToken.None);

        var consolidado = Assert.Single(dbContext.DailyConsolidations);
        Assert.Equal(100m, consolidado.Creditos);
        Assert.Equal(30m, consolidado.Debitos);
        Assert.Equal(70m, consolidado.Saldo);
    }

    [Fact]
    public async Task Eventos_de_dias_diferentes_criam_consolidados_separados()
    {
        using var dbContext = CriarDbContext();
        var repository = new DailyConsolidationRepository(dbContext);

        await repository.AplicarLancamentoAsync(Guid.NewGuid(), new DateOnly(2026, 9, 4), TipoLancamento.Credito, 100m, CancellationToken.None);
        await repository.AplicarLancamentoAsync(Guid.NewGuid(), new DateOnly(2026, 9, 5), TipoLancamento.Credito, 50m, CancellationToken.None);

        Assert.Equal(2, dbContext.DailyConsolidations.Count());
    }

    [Fact]
    public async Task GetByDataAsync_retorna_a_posicao_do_dia_com_lancamentos_processados()
    {
        using var dbContext = CriarDbContext();
        var repository = new DailyConsolidationRepository(dbContext);
        var data = new DateOnly(2026, 9, 4);
        await repository.AplicarLancamentoAsync(Guid.NewGuid(), data, TipoLancamento.Credito, 100m, CancellationToken.None);
        await repository.AplicarLancamentoAsync(Guid.NewGuid(), data, TipoLancamento.Debito, 30m, CancellationToken.None);

        var consolidado = await repository.GetByDataAsync(data, CancellationToken.None);

        Assert.NotNull(consolidado);
        Assert.Equal(data, consolidado!.Data);
        Assert.Equal(100m, consolidado.Creditos);
        Assert.Equal(30m, consolidado.Debitos);
        Assert.Equal(70m, consolidado.Saldo);
    }

    [Fact]
    public async Task GetByDataAsync_retorna_null_para_dia_sem_nenhum_lancamento_processado()
    {
        using var dbContext = CriarDbContext();
        var repository = new DailyConsolidationRepository(dbContext);

        var consolidado = await repository.GetByDataAsync(new DateOnly(2026, 9, 4), CancellationToken.None);

        Assert.Null(consolidado);
    }

    [Fact]
    public async Task GetByPeriodoAsync_retorna_os_consolidados_do_intervalo_ordenados_por_data()
    {
        using var dbContext = CriarDbContext();
        var repository = new DailyConsolidationRepository(dbContext);
        await repository.AplicarLancamentoAsync(Guid.NewGuid(), new DateOnly(2026, 9, 5), TipoLancamento.Credito, 50m, CancellationToken.None);
        await repository.AplicarLancamentoAsync(Guid.NewGuid(), new DateOnly(2026, 9, 3), TipoLancamento.Credito, 100m, CancellationToken.None);
        await repository.AplicarLancamentoAsync(Guid.NewGuid(), new DateOnly(2026, 9, 10), TipoLancamento.Credito, 10m, CancellationToken.None);

        var consolidados = await repository.GetByPeriodoAsync(new DateOnly(2026, 9, 3), new DateOnly(2026, 9, 5), CancellationToken.None);

        Assert.Equal(2, consolidados.Count);
        Assert.Equal(new DateOnly(2026, 9, 3), consolidados[0].Data);
        Assert.Equal(new DateOnly(2026, 9, 5), consolidados[1].Data);
    }

    [Fact]
    public async Task GetByPeriodoAsync_retorna_lista_vazia_quando_nenhum_dia_do_intervalo_tem_registro()
    {
        using var dbContext = CriarDbContext();
        var repository = new DailyConsolidationRepository(dbContext);
        await repository.AplicarLancamentoAsync(Guid.NewGuid(), new DateOnly(2026, 9, 20), TipoLancamento.Credito, 100m, CancellationToken.None);

        var consolidados = await repository.GetByPeriodoAsync(new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 5), CancellationToken.None);

        Assert.Empty(consolidados);
    }
}
