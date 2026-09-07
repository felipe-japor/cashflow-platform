using Consolidado.Domain;

namespace Consolidado.Application.Ports;

/// <summary>
/// Porta de persistência do agregado <see cref="DailyConsolidation"/>. Repositório fino por
/// agregado (sem IRepository&lt;T&gt; genérico — decisão de estrutura da issue #6): o único
/// método expõe a operação de negócio real (aplicar o delta de um lançamento já consumido),
/// não um CRUD genérico, e encapsula o invariante de idempotência via inbox pattern
/// (docs/domain-mapping.md) — aplicar o delta e marcar o evento como processado acontecem
/// atomicamente dentro da mesma implementação, não em duas transações coordenadas de fora.
/// </summary>
/// <remarks>
/// A implementação completa (tabela de eventos processados, constraint única em event_id,
/// mesma transação atômica) é escopo da issue #12.
/// </remarks>
public interface IDailyConsolidationRepository
{
    /// <param name="eventId">Identidade do evento consumido (TransactionId) — chave de idempotência no inbox.</param>
    /// <param name="data">Dia de negócio ao qual o delta pertence.</param>
    /// <param name="tipo">Crédito ou débito.</param>
    /// <param name="valor">Delta a aplicar.</param>
    Task AplicarLancamentoAsync(Guid eventId, DateOnly data, TipoLancamento tipo, decimal valor, CancellationToken cancellationToken);

    /// <summary>
    /// Posição consolidada de um dia específico (RF04), ou <c>null</c> se nenhum lançamento
    /// para essa data foi processado ainda — não é responsabilidade do repositório decidir se
    /// isso é ou não um erro (essa decisão é da borda de API, issue #14).
    /// </summary>
    Task<DailyConsolidation?> GetByDataAsync(DateOnly data, CancellationToken cancellationToken);

    /// <summary>
    /// Consolidados com <see cref="DailyConsolidation.Data"/> dentro do intervalo (inclusive nas
    /// duas pontas), ordenados por data (RF04). Só os dias com registro processado aparecem —
    /// não há zero-preenchimento de dias sem lançamento, mesmo espírito de RF02 (ausência de
    /// dado não é erro).
    /// </summary>
    Task<IReadOnlyList<DailyConsolidation>> GetByPeriodoAsync(DateOnly dataInicial, DateOnly dataFinal, CancellationToken cancellationToken);
}
