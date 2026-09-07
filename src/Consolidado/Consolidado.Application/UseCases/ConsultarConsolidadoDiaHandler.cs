using Consolidado.Application.Ports;
using Consolidado.Domain;

namespace Consolidado.Application.UseCases;

/// <summary>
/// Caso de uso de RF04 (consultar posição consolidada de um dia): delega a consulta a
/// <see cref="IDailyConsolidationRepository"/>. Classe simples em vez de MediatR — mesmo padrão
/// adotado em <see cref="ConsultarConsolidadoPeriodoHandler"/> e em
/// <c>Lancamentos.Application.UseCases.ConsultarLancamentosHandler</c> (KISS).
/// </summary>
/// <remarks>
/// Retorna <c>null</c> quando nenhum lançamento para a data foi processado ainda — a decisão
/// entre 404 e um objeto zerado é da borda de API (issue #14), não do caso de uso.
/// </remarks>
public class ConsultarConsolidadoDiaHandler(IDailyConsolidationRepository repository)
{
    public Task<DailyConsolidation?> Handle(ConsultarConsolidadoDiaCommand command, CancellationToken cancellationToken) =>
        repository.GetByDataAsync(command.Data, cancellationToken);
}
