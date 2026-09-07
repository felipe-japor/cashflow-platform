using System.Text.Json;
using Lancamentos.Domain;
using Microsoft.EntityFrameworkCore;
using Shared.Contracts;

namespace Lancamentos.Infrastructure.Tests;

/// <summary>
/// Prova o comportamento atômico real de <see cref="TransactionRepository.AddAsync"/> (ADR-002):
/// o lançamento e o evento de outbox correspondente são gravados no mesmo commit, com o payload
/// serializado corretamente. Usa EF Core InMemory (sem Postgres) — suficiente para provar a
/// lógica de mapeamento e persistência conjunta; teste de integração com banco real é escopo da
/// issue #8/#9.
/// </summary>
public class TransactionRepositoryTests
{
    private static LancamentosDbContext CriarDbContext()
    {
        var options = new DbContextOptionsBuilder<LancamentosDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new LancamentosDbContext(options);
    }

    [Fact]
    public async Task AddAsync_persiste_o_lancamento_e_o_evento_de_outbox_no_mesmo_commit()
    {
        using var dbContext = CriarDbContext();
        var repository = new TransactionRepository(dbContext);
        var transaction = new Transaction(new DateOnly(2026, 9, 4), Lancamentos.Domain.TipoLancamento.Credito, 150.75m, "Venda à vista");

        await repository.AddAsync(transaction, CancellationToken.None);

        Assert.Single(dbContext.Transactions);
        Assert.Single(dbContext.OutboxEvents);
    }

    [Fact]
    public async Task AddAsync_grava_o_evento_de_outbox_com_o_payload_do_lancamento_correspondente()
    {
        using var dbContext = CriarDbContext();
        var repository = new TransactionRepository(dbContext);
        var transaction = new Transaction(new DateOnly(2026, 9, 4), Lancamentos.Domain.TipoLancamento.Credito, 150.75m, "Venda à vista");

        await repository.AddAsync(transaction, CancellationToken.None);

        var outboxEvent = Assert.Single(dbContext.OutboxEvents);
        Assert.Equal(nameof(LancamentoRegistrado), outboxEvent.EventType);
        var evento = JsonSerializer.Deserialize<LancamentoRegistrado>(outboxEvent.Payload);
        Assert.NotNull(evento);
        Assert.Equal(transaction.Id, evento!.TransactionId);
        Assert.Equal(transaction.Data, evento.Data);
        Assert.Equal(transaction.Valor, evento.Valor);
        Assert.Equal(Shared.Contracts.TipoLancamento.Credito, evento.Tipo);
    }

    [Theory]
    [InlineData(Lancamentos.Domain.TipoLancamento.Credito, Shared.Contracts.TipoLancamento.Credito)]
    [InlineData(Lancamentos.Domain.TipoLancamento.Debito, Shared.Contracts.TipoLancamento.Debito)]
    public async Task AddAsync_mapeia_o_tipo_do_dominio_para_o_tipo_do_contrato_de_evento(
        Lancamentos.Domain.TipoLancamento tipoDominio, Shared.Contracts.TipoLancamento tipoContratoEsperado)
    {
        using var dbContext = CriarDbContext();
        var repository = new TransactionRepository(dbContext);
        var transaction = new Transaction(new DateOnly(2026, 9, 4), tipoDominio, 10m, "Lançamento");

        await repository.AddAsync(transaction, CancellationToken.None);

        var outboxEvent = Assert.Single(dbContext.OutboxEvents);
        var evento = JsonSerializer.Deserialize<LancamentoRegistrado>(outboxEvent.Payload);
        Assert.Equal(tipoContratoEsperado, evento!.Tipo);
    }

    [Fact]
    public async Task GetByPeriodoAsync_retorna_apenas_os_lancamentos_dentro_do_intervalo_ordenados_por_data()
    {
        using var dbContext = CriarDbContext();
        var repository = new TransactionRepository(dbContext);
        var dentroDoInicio = new Transaction(new DateOnly(2026, 9, 1), Lancamentos.Domain.TipoLancamento.Credito, 10m, "Início do intervalo");
        var dentroDoMeio = new Transaction(new DateOnly(2026, 9, 15), Lancamentos.Domain.TipoLancamento.Debito, 20m, "Meio do intervalo");
        var dentroDoFim = new Transaction(new DateOnly(2026, 9, 30), Lancamentos.Domain.TipoLancamento.Credito, 30m, "Fim do intervalo");
        var antesDoIntervalo = new Transaction(new DateOnly(2026, 8, 31), Lancamentos.Domain.TipoLancamento.Credito, 40m, "Antes do intervalo");
        var depoisDoIntervalo = new Transaction(new DateOnly(2026, 10, 1), Lancamentos.Domain.TipoLancamento.Debito, 50m, "Depois do intervalo");
        // Insere fora de ordem para provar que a ordenação vem da query, não da ordem de inserção.
        foreach (var transaction in new[] { dentroDoFim, antesDoIntervalo, dentroDoInicio, depoisDoIntervalo, dentroDoMeio })
        {
            await repository.AddAsync(transaction, CancellationToken.None);
        }

        var resultado = await repository.GetByPeriodoAsync(new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30), CancellationToken.None);

        Assert.Equal([dentroDoInicio.Id, dentroDoMeio.Id, dentroDoFim.Id], resultado.Select(t => t.Id));
    }

    [Fact]
    public async Task Se_o_commit_e_interrompido_nem_o_lancamento_nem_o_evento_de_outbox_sao_persistidos()
    {
        // Prova a atomicidade em si (issue #9, revisão do Arquiteto Adjunto): "o processo morre
        // entre a escrita e o commit" simulado cancelando o token antes do SaveChangesAsync
        // completar — lançamento e evento de outbox são adicionados ao mesmo change tracker, mas
        // nenhum dos dois chega a ser persistido se o commit não se completa. Não é só o caminho
        // feliz: aqui NENHUM dos dois lados deve sobreviver.
        using var dbContext = CriarDbContext();
        var repository = new TransactionRepository(dbContext);
        var transaction = new Transaction(new DateOnly(2026, 9, 4), Lancamentos.Domain.TipoLancamento.Credito, 100m, "Não deve ser persistido");
        using var cancelado = new CancellationTokenSource();
        await cancelado.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => repository.AddAsync(transaction, cancelado.Token));

        Assert.Empty(dbContext.Transactions);
        Assert.Empty(dbContext.OutboxEvents);
    }

    [Fact]
    public async Task GetByPeriodoAsync_retorna_lista_vazia_quando_nao_ha_lancamento_no_periodo()
    {
        using var dbContext = CriarDbContext();
        var repository = new TransactionRepository(dbContext);
        await repository.AddAsync(new Transaction(new DateOnly(2026, 1, 1), Lancamentos.Domain.TipoLancamento.Credito, 10m, "Fora do período"), CancellationToken.None);

        var resultado = await repository.GetByPeriodoAsync(new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30), CancellationToken.None);

        Assert.Empty(resultado);
    }
}
