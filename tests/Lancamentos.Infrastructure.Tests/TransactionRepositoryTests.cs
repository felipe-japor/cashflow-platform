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
}
