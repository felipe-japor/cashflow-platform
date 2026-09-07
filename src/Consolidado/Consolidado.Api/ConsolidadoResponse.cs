using Consolidado.Domain;

namespace Consolidado.Api;

/// <summary>Corpo da resposta dos endpoints de consulta do Consolidado (RF04).</summary>
public sealed record ConsolidadoResponse(DateOnly Data, decimal Creditos, decimal Debitos, decimal Saldo)
{
    public static ConsolidadoResponse De(DailyConsolidation consolidado) => new(
        consolidado.Data,
        consolidado.Creditos,
        consolidado.Debitos,
        consolidado.Saldo);
}
