using Lancamentos.Domain.Exceptions;

namespace Lancamentos.Domain;

/// <summary>
/// Lançamento de débito ou crédito no fluxo de caixa. Fonte da verdade do domínio Lançamentos
/// (docs/domain-mapping.md).
/// </summary>
/// <remarks>
/// O construtor é o único ponto de criação (sem setters públicos) e garante o invariante de
/// RF01 — valor &gt; 0 e tipo dentro do domínio conhecido — antes de qualquer I/O, lançando
/// <see cref="LancamentoInvalidoException"/> quando violado. "Nada é persistido" no caso
/// inválido decorre diretamente disso: a validação acontece em memória, antes de qualquer
/// chamada ao repositório (issue #7).
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
        if (valor <= 0)
        {
            throw new LancamentoInvalidoException("O valor do lançamento deve ser maior que zero.");
        }

        if (!Enum.IsDefined(tipo))
        {
            throw new LancamentoInvalidoException($"Tipo de lançamento inválido: '{tipo}'.");
        }

        Id = Guid.NewGuid();
        Data = data;
        Tipo = tipo;
        Valor = valor;
        Descricao = descricao;
    }
}
