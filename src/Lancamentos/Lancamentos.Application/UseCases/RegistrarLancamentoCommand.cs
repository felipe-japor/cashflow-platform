using Lancamentos.Domain;

namespace Lancamentos.Application.UseCases;

/// <summary>
/// Comando de entrada do caso de uso "registrar lançamento" (RF01). Carrega os dados brutos
/// recebidos pela borda (API) até o handler, que é quem decide construir/validar o agregado.
/// </summary>
public sealed record RegistrarLancamentoCommand(DateOnly Data, TipoLancamento Tipo, decimal Valor, string Descricao);
