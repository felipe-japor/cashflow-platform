namespace Lancamentos.Domain;

/// <summary>
/// Lançamento de débito ou crédito no fluxo de caixa. Fonte da verdade do domínio Lançamentos
/// (docs/domain-mapping.md).
/// </summary>
/// <remarks>
/// Guard clauses e regras de validação completas (ex.: valor &gt; 0) são escopo da issue #7
/// (cadastro e validação) — aqui a entidade só carrega o shape necessário para os projetos
/// compilarem e a persistência (EF Core, issue #8) e o DI resolverem de verdade.
/// </remarks>
public class Transaction
{
    public Guid Id { get; private set; }
    public DateOnly Data { get; private set; }
    public TipoLancamento Tipo { get; private set; }
    public decimal Valor { get; private set; }
    public string Descricao { get; private set; }

    // Construtor sem parâmetros exigido pelo EF Core para materializar a entidade.
    private Transaction()
    {
        Descricao = string.Empty;
    }

    public Transaction(DateOnly data, TipoLancamento tipo, decimal valor, string descricao)
    {
        Id = Guid.NewGuid();
        Data = data;
        Tipo = tipo;
        Valor = valor;
        Descricao = descricao;
    }
}
