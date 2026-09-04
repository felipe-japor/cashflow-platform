namespace Lancamentos.Domain;

/// <summary>
/// Crédito ou débito. O sinal do tipo — não o sinal do valor — define a natureza do lançamento
/// (docs/domain-mapping.md).
/// </summary>
public enum TipoLancamento
{
    Credito,
    Debito
}
