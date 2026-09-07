using Consolidado.Application.Ports;
using Consolidado.Domain;
using Microsoft.EntityFrameworkCore;

namespace Consolidado.Infrastructure;

/// <summary>
/// Implementação de <see cref="IDailyConsolidationRepository"/> via EF Core/Npgsql. Verifica o
/// inbox (<see cref="ProcessedEvent"/>), aplica o delta ao <see cref="DailyConsolidation"/> do
/// dia e marca o evento como processado no mesmo
/// <see cref="Microsoft.EntityFrameworkCore.DbContext.SaveChangesAsync(CancellationToken)"/> —
/// um único commit garante a idempotência do consumo (docs/domain-mapping.md).
/// </summary>
/// <remarks>
/// Nota sobre concorrência (issue #12, revisão do Arquiteto Adjunto): a checagem "já processado?"
/// abaixo, sozinha, tem uma janela de corrida sob redelivery concorrente — duas tentativas podem
/// passar por ela antes de qualquer uma commitar. Quem fecha essa janela de verdade é a
/// constraint de unicidade no banco (chave primária de <see cref="ProcessedEvent.EventId"/>,
/// <c>ConsolidadoDbContext</c>), não a checagem em si: a perdedora da corrida tem todo o seu
/// <c>SaveChangesAsync</c> (delta + inbox, mesma transação) rejeitado e revertido — nunca há
/// delta duplicado, mesmo sob concorrência real. A perdedora recebe uma exceção; ela não é
/// silenciada aqui de propósito — o chamador (worker de consumo, issue #12) já tem retry curto
/// para falha transitória, e uma nova tentativa encontra o evento já marcado como processado
/// pela vencedora, completando como no-op. Cobertura de teste de integração completa com banco
/// real são das issues #8/#12 (Testcontainers fora do orçamento sem pedido explícito, mesmo
/// racional de <c>TransactionRepositoryTests</c>).
/// </remarks>
public class DailyConsolidationRepository(ConsolidadoDbContext dbContext) : IDailyConsolidationRepository
{
    public async Task AplicarLancamentoAsync(Guid eventId, DateOnly data, TipoLancamento tipo, decimal valor, CancellationToken cancellationToken)
    {
        var jaProcessado = await dbContext.ProcessedEvents.AnyAsync(e => e.EventId == eventId, cancellationToken);
        if (jaProcessado)
        {
            return;
        }

        var consolidado = await dbContext.DailyConsolidations.SingleOrDefaultAsync(d => d.Data == data, cancellationToken);
        if (consolidado is null)
        {
            consolidado = new DailyConsolidation(data);
            dbContext.DailyConsolidations.Add(consolidado);
        }

        consolidado.Aplicar(tipo, valor);
        dbContext.ProcessedEvents.Add(new ProcessedEvent(eventId));

        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
