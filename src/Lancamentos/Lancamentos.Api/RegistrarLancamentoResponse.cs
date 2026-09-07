using Lancamentos.Domain;

namespace Lancamentos.Api;

/// <summary>Corpo da resposta 201 de <c>POST /lancamentos</c> (RF01).</summary>
public sealed record RegistrarLancamentoResponse(Guid Id, DateOnly Data, TipoLancamento Tipo, decimal Valor, string Descricao)
{
    public static RegistrarLancamentoResponse De(Transaction transaction) => new(
        transaction.Id,
        transaction.Data,
        transaction.Tipo,
        transaction.Valor,
        transaction.Descricao);
}
