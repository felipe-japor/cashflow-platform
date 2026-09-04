namespace Shared.Contracts;

/// <summary>
/// Tipo de um lançamento de fluxo de caixa, conforme carregado no contrato de integração
/// (evento <see cref="LancamentoRegistrado"/>). Cópia intencionalmente independente do enum
/// homônimo de cada domínio (Lancamentos.Domain, Consolidado.Domain): o contrato de evento
/// não deve acoplar os dois bounded contexts nem o domínio de Lançamentos a este projeto —
/// ver docs/domain-mapping.md e a decisão de estrutura da issue #6.
/// </summary>
public enum TipoLancamento
{
    Credito,
    Debito
}
