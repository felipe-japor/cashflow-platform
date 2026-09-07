using Lancamentos.Application.Ports;
using Lancamentos.Domain;

namespace Lancamentos.Application.UseCases;

/// <summary>
/// Caso de uso de RF01 (registrar lançamento): constrói/valida o agregado <see cref="Transaction"/>
/// e delega a persistência atômica (lançamento + evento de outbox, ADR-002) a
/// <see cref="ITransactionRepository"/>. Classe simples em vez de MediatR/infraestrutura de CQRS —
/// um único caso de uso não justifica esse peso adicional (KISS).
/// </summary>
/// <remarks>
/// A validação de domínio acontece dentro do construtor de <see cref="Transaction"/>, antes de
/// qualquer chamada ao repositório — se os dados forem inválidos, a exceção é lançada e
/// <c>AddAsync</c> nunca é chamado, garantindo que nada é persistido (critério de aceite de RF01).
/// </remarks>
public class RegistrarLancamentoHandler(ITransactionRepository repository)
{
    public async Task<Transaction> Handle(RegistrarLancamentoCommand command, CancellationToken cancellationToken)
    {
        var transaction = new Transaction(command.Data, command.Tipo, command.Valor, command.Descricao);

        await repository.AddAsync(transaction, cancellationToken);

        return transaction;
    }
}
