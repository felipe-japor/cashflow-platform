using System.Text.Json;
using Lancamentos.Application.Ports;
using Lancamentos.Domain;
using Microsoft.EntityFrameworkCore;
using DomainTipoLancamento = Lancamentos.Domain.TipoLancamento;
using ContratoTipoLancamento = Shared.Contracts.TipoLancamento;

namespace Lancamentos.Infrastructure;

/// <summary>
/// Implementação de <see cref="ITransactionRepository"/> via EF Core/Npgsql. Grava o lançamento
/// e o evento de outbox correspondente no mesmo <see cref="Microsoft.EntityFrameworkCore.DbContext.SaveChangesAsync(CancellationToken)"/> —
/// um único commit garante a atomicidade exigida pelo ADR-002 sem precisar de uma transação de
/// banco explícita.
/// </summary>
/// <remarks>
/// Os testes desta classe usam EF Core InMemory (rápidos, sem dependência de infraestrutura) —
/// suficiente para provar a lógica de mapeamento/persistência conjunta em si; a prova de que o
/// mapeamento realmente funciona contra PostgreSQL vem da migration real aplicada (issue #8,
/// ver <c>Migrations/</c> e o startup do serviço) e da execução via docker-compose, não de um
/// teste automatizado com banco real — Testcontainers ficaria fora do orçamento desta issue sem
/// pedido explícito.
/// </remarks>
public class TransactionRepository(LancamentosDbContext dbContext) : ITransactionRepository
{
    public async Task AddAsync(Transaction transaction, CancellationToken cancellationToken)
    {
        dbContext.Transactions.Add(transaction);

        var evento = new Shared.Contracts.LancamentoRegistrado(
            transaction.Id,
            transaction.Data,
            Map(transaction.Tipo),
            transaction.Valor);
        var payload = JsonSerializer.Serialize(evento);
        dbContext.OutboxEvents.Add(new OutboxEvent(nameof(Shared.Contracts.LancamentoRegistrado), payload));

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Transaction>> GetByPeriodoAsync(DateOnly dataInicial, DateOnly dataFinal, CancellationToken cancellationToken) =>
        await dbContext.Transactions
            .Where(t => t.Data >= dataInicial && t.Data <= dataFinal)
            .OrderBy(t => t.Data)
            .ToListAsync(cancellationToken);

    private static ContratoTipoLancamento Map(DomainTipoLancamento tipo) => tipo switch
    {
        DomainTipoLancamento.Credito => ContratoTipoLancamento.Credito,
        DomainTipoLancamento.Debito => ContratoTipoLancamento.Debito,
        _ => throw new ArgumentOutOfRangeException(nameof(tipo), tipo, null)
    };
}
