using Lancamentos.Application.Ports;
using Lancamentos.Application.UseCases;
using Lancamentos.Domain;

namespace Lancamentos.Application.Tests;

/// <summary>
/// Prova o comportamento do caso de uso de RF02 isolado do host/HTTP: o handler apenas delega ao
/// repositório o intervalo recebido.
/// </summary>
public class ConsultarLancamentosHandlerTests
{
    private sealed class RepositorioFalso : ITransactionRepository
    {
        public IReadOnlyList<Transaction> Resultado { get; set; } = [];
        public DateOnly? DataInicialRecebida { get; private set; }
        public DateOnly? DataFinalRecebida { get; private set; }

        public Task AddAsync(Transaction transaction, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<IReadOnlyList<Transaction>> GetByPeriodoAsync(DateOnly dataInicial, DateOnly dataFinal, CancellationToken cancellationToken)
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
        var handler = new ConsultarLancamentosHandler(repository);
        var command = new ConsultarLancamentosCommand(new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30));

        await handler.Handle(command, CancellationToken.None);

        Assert.Equal(command.DataInicial, repository.DataInicialRecebida);
        Assert.Equal(command.DataFinal, repository.DataFinalRecebida);
    }

    [Fact]
    public async Task Handle_retorna_a_lista_vazia_do_repositorio_quando_nao_ha_lancamento_no_periodo()
    {
        var repository = new RepositorioFalso { Resultado = [] };
        var handler = new ConsultarLancamentosHandler(repository);
        var command = new ConsultarLancamentosCommand(new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30));

        var resultado = await handler.Handle(command, CancellationToken.None);

        Assert.Empty(resultado);
    }
}
