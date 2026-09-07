using Lancamentos.Application.Ports;
using Lancamentos.Domain;

namespace Lancamentos.Application.UseCases;

/// <summary>
/// Caso de uso de RF02 (consultar lançamentos por período): delega a consulta a
/// <see cref="ITransactionRepository"/>. Classe simples em vez de MediatR — mesmo padrão adotado
/// em <see cref="RegistrarLancamentoHandler"/> (KISS).
/// </summary>
/// <remarks>
/// Sem validação de intervalo aqui de propósito: se <c>dataInicial</c> for posterior a
/// <c>dataFinal</c>, a consulta simplesmente não encontra nenhum lançamento que satisfaça as duas
/// condições e retorna lista vazia — o mesmo comportamento exigido por RF02 para "nenhum
/// lançamento no período", sem precisar de uma regra de validação adicional.
/// </remarks>
public class ConsultarLancamentosHandler(ITransactionRepository repository)
{
    public Task<IReadOnlyList<Transaction>> Handle(ConsultarLancamentosCommand command, CancellationToken cancellationToken) =>
        repository.GetByPeriodoAsync(command.DataInicial, command.DataFinal, cancellationToken);
}
