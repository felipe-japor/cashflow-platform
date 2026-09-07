using System.Text.Json;
using Lancamentos.Application.Ports;
using Lancamentos.Domain;
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
/// Escopo desta issue (#6): shape funcional mínimo, provando que a porta é satisfeita e o DI
/// resolve. Índices/constraints de schema e cobertura de teste de integração completa com banco
/// real são das issues #8/#9.
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

    private static ContratoTipoLancamento Map(DomainTipoLancamento tipo) => tipo switch
    {
        DomainTipoLancamento.Credito => ContratoTipoLancamento.Credito,
        DomainTipoLancamento.Debito => ContratoTipoLancamento.Debito,
        _ => throw new ArgumentOutOfRangeException(nameof(tipo), tipo, null)
    };
}
