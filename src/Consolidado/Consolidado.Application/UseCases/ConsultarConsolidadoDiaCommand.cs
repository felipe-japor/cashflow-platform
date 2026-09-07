namespace Consolidado.Application.UseCases;

/// <summary>
/// Comando de entrada do caso de uso "consultar posição consolidada de um dia" (RF04). Carrega a
/// data recebida pela borda (API) até o handler.
/// </summary>
public sealed record ConsultarConsolidadoDiaCommand(DateOnly Data);
