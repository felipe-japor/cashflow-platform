namespace Lancamentos.Domain.Exceptions;

/// <summary>
/// Lançamento com dado inválido segundo as regras de RF01 (valor ≤ 0 ou tipo fora do domínio
/// conhecido). Exceção de domínio própria — não uma <see cref="ArgumentException"/> genérica —
/// para a borda HTTP (Lancamentos.Api) conseguir distinguir "erro de validação de negócio" de
/// qualquer outra falha e mapear para 400 de forma limpa.
/// </summary>
public sealed class LancamentoInvalidoException : Exception
{
    public LancamentoInvalidoException(string message) : base(message)
    {
    }
}
