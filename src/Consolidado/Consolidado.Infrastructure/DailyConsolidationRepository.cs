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
/// Escopo desta issue (#6): shape funcional mínimo, provando que a porta é satisfeita e o DI
/// resolve. Cobertura de teste de integração completa com banco real, concorrência e reentrega
/// são das issues #8/#12.
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
