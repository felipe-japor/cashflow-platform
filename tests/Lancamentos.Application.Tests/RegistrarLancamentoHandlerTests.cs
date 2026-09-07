using Lancamentos.Application.Ports;
using Lancamentos.Application.UseCases;
using Lancamentos.Domain;
using Lancamentos.Domain.Exceptions;

namespace Lancamentos.Application.Tests;

/// <summary>
/// Prova o comportamento do caso de uso de RF01 isolado do host/HTTP: o handler valida via o
/// construtor de <see cref="Transaction"/> antes de chamar o repositório, e nada é persistido
/// quando a validação falha.
/// </summary>
public class RegistrarLancamentoHandlerTests
{
    private sealed class RepositorioFalso : ITransactionRepository
    {
        public List<Transaction> Persistidos { get; } = [];

        public Task AddAsync(Transaction transaction, CancellationToken cancellationToken)
        {
            Persistidos.Add(transaction);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<Transaction>> GetByPeriodoAsync(DateOnly dataInicial, DateOnly dataFinal, CancellationToken cancellationToken) =>
            throw new NotImplementedException("Não utilizado pelos testes de RegistrarLancamentoHandler (RF01).");
    }

    [Fact]
    public async Task Handle_persiste_o_lancamento_quando_os_dados_sao_validos()
    {
        var repository = new RepositorioFalso();
        var handler = new RegistrarLancamentoHandler(repository);
        var command = new RegistrarLancamentoCommand(new DateOnly(2026, 9, 7), TipoLancamento.Credito, 150.75m, "Venda à vista");

        var transaction = await handler.Handle(command, CancellationToken.None);

        Assert.Single(repository.Persistidos);
        Assert.Equal(transaction.Id, repository.Persistidos[0].Id);
    }

    [Fact]
    public async Task Handle_nao_chama_o_repositorio_quando_o_valor_e_menor_ou_igual_a_zero()
    {
        var repository = new RepositorioFalso();
        var handler = new RegistrarLancamentoHandler(repository);
        var command = new RegistrarLancamentoCommand(new DateOnly(2026, 9, 7), TipoLancamento.Debito, 0m, "Lançamento inválido");

        await Assert.ThrowsAsync<LancamentoInvalidoException>(() => handler.Handle(command, CancellationToken.None));

        Assert.Empty(repository.Persistidos);
    }
}
