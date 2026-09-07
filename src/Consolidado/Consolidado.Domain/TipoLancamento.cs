namespace Consolidado.Domain;

/// <summary>
/// Crédito ou débito — determina se um delta consumido do evento
/// <c>LancamentoRegistrado</c> soma em <see cref="DailyConsolidation.Creditos"/> ou
/// <see cref="DailyConsolidation.Debitos"/> (docs/domain-mapping.md).
/// </summary>
public enum TipoLancamento
{
    Credito,
    Debito
}
