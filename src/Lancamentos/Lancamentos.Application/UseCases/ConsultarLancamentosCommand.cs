namespace Lancamentos.Application.UseCases;

/// <summary>
/// Comando de entrada do caso de uso "consultar lançamentos por período" (RF02). Carrega o
/// intervalo de datas recebido pela borda (API) até o handler.
/// </summary>
public sealed record ConsultarLancamentosCommand(DateOnly DataInicial, DateOnly DataFinal);
