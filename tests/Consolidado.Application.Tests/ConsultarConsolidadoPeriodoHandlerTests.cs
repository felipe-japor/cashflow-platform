using Consolidado.Application.Ports;
using Consolidado.Application.UseCases;
using Consolidado.Domain;

namespace Consolidado.Application.Tests;

/// <summary>
/// Prova o comportamento do caso de uso de RF04 (posição por período) isolado do host/HTTP: o
/// handler apenas delega ao repositório o intervalo recebido.
/// </summary>
public class ConsultarConsolidadoPeriodoHandlerTests
{
    private sealed class RepositorioFalso : IDailyConsolidationRepository
    {
        public IReadOnlyList<DailyConsolidation> Resultado { get; set; } = [];
        public DateOnly? DataInicialRecebida { get; private set; }
        public DateOnly? DataFinalRecebida { get; private set; }

        public Task AplicarLancamentoAsync(Guid eventId, DateOnly data, TipoLancamento tipo, decimal valor, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task<DailyConsolidation?> GetByDataAsync(DateOnly data, CancellationToken cancellationToken) =>
            Task.FromResult<DailyConsolidation?>(null);

        public Task<IReadOnlyList<DailyConsolidation>> GetByPeriodoAsync(DateOnly dataInicial, DateOnly dataFinal, CancellationToken cancellationToken)
        {
            DataInicialRecebida = dataInicial;
            DataFinalRecebida = dataFinal;
            return Task.FromResult(Resultado);
        }
    }

    [Fact]
    public async Task Handle_repassa_o_intervalo_recebido_ao_repositorio()
    {
        var repository = new RepositorioFalso();
        var handler = new ConsultarConsolidadoPeriodoHandler(repository);
        var command = new ConsultarConsolidadoPeriodoCommand(new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30));

        await handler.Handle(command, CancellationToken.None);

        Assert.Equal(command.DataInicial, repository.DataInicialRecebida);
        Assert.Equal(command.DataFinal, repository.DataFinalRecebida);
    }

    [Fact]
    public async Task Handle_retorna_a_lista_vazia_do_repositorio_quando_nao_ha_consolidado_no_periodo()
    {
        var repository = new RepositorioFalso { Resultado = [] };
        var handler = new ConsultarConsolidadoPeriodoHandler(repository);
        var command = new ConsultarConsolidadoPeriodoCommand(new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30));

        var resultado = await handler.Handle(command, CancellationToken.None);

        Assert.Empty(resultado);
    }
}
