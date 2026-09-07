namespace Consolidado.Domain;

/// <summary>
/// Saldo consolidado de um dia de negócio. Read model do domínio Consolidado — não é dono de
/// nenhuma regra sobre o que é um lançamento válido, só nettar créditos e débitos do dia a partir
/// dos eventos consumidos (docs/domain-mapping.md).
/// </summary>
/// <remarks>
/// A aplicação idempotente do delta via inbox pattern (constraint única em <c>event_id</c>,
/// mesma transação atômica) é escopo da issue #12 — aqui a entidade só carrega o shape mapeado
/// (Creditos/Debitos/Saldo armazenados, não calculados on-the-fly) e o método de aplicação de
/// delta que expressa a regra de netting em si.
/// </remarks>
public class DailyConsolidation
{
    public Guid Id { get; private set; }
    public DateOnly Data { get; private set; }
    public decimal Creditos { get; private set; }
    public decimal Debitos { get; private set; }
    public decimal Saldo { get; private set; }

    // Construtor sem parâmetros exigido pelo EF Core para materializar a entidade.
    private DailyConsolidation()
    {
    }

    public DailyConsolidation(DateOnly data)
    {
        Id = Guid.NewGuid();
        Data = data;
    }

    /// <summary>
    /// Aplica o delta de um lançamento já consumido ao saldo do dia. Netting incremental
    /// (soma de novo a cada chamada) — não é idempotente por si só; a idempotência do consumo
    /// vem do inbox pattern na camada de infraestrutura (issue #12), não daqui.
    /// </summary>
    public void Aplicar(TipoLancamento tipo, decimal valor)
    {
        if (tipo == TipoLancamento.Credito)
        {
            Creditos += valor;
        }
        else
        {
            Debitos += valor;
        }

        Saldo = Creditos - Debitos;
    }
}
