using Consolidado.Application.Ports;
using Consolidado.Domain;

namespace Consolidado.Application.UseCases;

/// <summary>
/// Caso de uso de RF04 (consultar posição consolidada por período): delega a consulta a
/// <see cref="IDailyConsolidationRepository"/>. Classe simples em vez de MediatR — mesmo padrão
/// adotado em <c>Lancamentos.Application.UseCases.ConsultarLancamentosHandler</c> (KISS).
/// </summary>
/// <remarks>
/// Sem validação de intervalo aqui de propósito: se <c>dataInicial</c> for posterior a
/// <c>dataFinal</c>, a consulta simplesmente não encontra nenhum consolidado que satisfaça as
/// duas condições e retorna lista vazia — mesmo racional de
/// <c>ConsultarLancamentosHandler</c>. Dias sem lançamento processado dentro do intervalo não
/// aparecem na lista (sem zero-preenchimento, mesmo espírito de RF02).
/// </remarks>
public class ConsultarConsolidadoPeriodoHandler(IDailyConsolidationRepository repository)
{
    public Task<IReadOnlyList<DailyConsolidation>> Handle(ConsultarConsolidadoPeriodoCommand command, CancellationToken cancellationToken) =>
        repository.GetByPeriodoAsync(command.DataInicial, command.DataFinal, cancellationToken);
}
