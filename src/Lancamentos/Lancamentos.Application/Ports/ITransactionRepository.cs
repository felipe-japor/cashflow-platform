using Lancamentos.Domain;

namespace Lancamentos.Application.Ports;

/// <summary>
/// Porta de persistência do agregado <see cref="Transaction"/>. Repositório fino por agregado
/// (sem IRepository&lt;T&gt; genérico — decisão de estrutura da issue #6): encapsula o invariante
/// transacional entre gravar o lançamento e publicar o evento correspondente na outbox
/// (ADR-002), não é um CRUD genérico.
/// </summary>
/// <remarks>
/// A implementação atômica completa (Transaction + registro de outbox na mesma transação de
/// banco) é escopo das issues #8 (persistência) e #9 (outbox transacional).
/// </remarks>
public interface ITransactionRepository
{
    Task AddAsync(Transaction transaction, CancellationToken cancellationToken);

    /// <summary>
    /// Lançamentos com <see cref="Transaction.Data"/> dentro do intervalo (inclusive nas duas
    /// pontas), ordenados por data (RF02). Nenhum lançamento no período retorna lista vazia —
    /// não é responsabilidade do repositório decidir se isso é ou não um erro.
    /// </summary>
    Task<IReadOnlyList<Transaction>> GetByPeriodoAsync(DateOnly dataInicial, DateOnly dataFinal, CancellationToken cancellationToken);
}
