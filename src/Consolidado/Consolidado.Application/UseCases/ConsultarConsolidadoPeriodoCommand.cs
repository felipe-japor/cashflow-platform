namespace Consolidado.Application.UseCases;

/// <summary>
/// Comando de entrada do caso de uso "consultar posição consolidada por período" (RF04). Carrega
/// o intervalo de datas recebido pela borda (API) até o handler.
/// </summary>
public sealed record ConsultarConsolidadoPeriodoCommand(DateOnly DataInicial, DateOnly DataFinal);
