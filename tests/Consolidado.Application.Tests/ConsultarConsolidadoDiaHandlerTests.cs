using Consolidado.Application.Ports;
using Consolidado.Application.UseCases;
using Consolidado.Domain;

namespace Consolidado.Application.Tests;

/// <summary>
/// Prova o comportamento do caso de uso de RF04 (posição de um dia) isolado do host/HTTP: o
/// handler apenas delega ao repositório a data recebida.
/// </summary>
public class ConsultarConsolidadoDiaHandlerTests
{
    private sealed class RepositorioFalso : IDailyConsolidationRepository
    {
        public DailyConsolidation? Resultado { get; set; }
        public DateOnly? DataRecebida { get; private set; }

        public Task AplicarLancamentoAsync(Guid eventId, DateOnly data, TipoLancamento tipo, decimal valor, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task<DailyConsolidation?> GetByDataAsync(DateOnly data, CancellationToken cancellationToken)
        {
            DataRecebida = data;
            return Task.FromResult(Resultado);
        }

        public Task<IReadOnlyList<DailyConsolidation>> GetByPeriodoAsync(DateOnly dataInicial, DateOnly dataFinal, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<DailyConsolidation>>([]);
    }

    [Fact]
    public async Task Handle_repassa_a_data_recebida_ao_repositorio()
    {
        var repository = new RepositorioFalso();
        var handler = new ConsultarConsolidadoDiaHandler(repository);
        var command = new ConsultarConsolidadoDiaCommand(new DateOnly(2026, 9, 4));

        await handler.Handle(command, CancellationToken.None);

        Assert.Equal(command.Data, repository.DataRecebida);
    }

    [Fact]
    public async Task Handle_retorna_null_do_repositorio_quando_o_dia_nao_tem_registro()
    {
        var repository = new RepositorioFalso { Resultado = null };
        var handler = new ConsultarConsolidadoDiaHandler(repository);
        var command = new ConsultarConsolidadoDiaCommand(new DateOnly(2026, 9, 4));

        var resultado = await handler.Handle(command, CancellationToken.None);

        Assert.Null(resultado);
    }
}
