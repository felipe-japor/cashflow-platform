using Consolidado.Domain;
using Microsoft.EntityFrameworkCore;

namespace Consolidado.Infrastructure;

public class ConsolidadoDbContext(DbContextOptions<ConsolidadoDbContext> options) : DbContext(options)
{
    public DbSet<DailyConsolidation> DailyConsolidations => Set<DailyConsolidation>();
    public DbSet<ProcessedEvent> ProcessedEvents => Set<ProcessedEvent>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Mapeamento mínimo para o esqueleto compilar e migrar (issue #6). Índice único em Data,
        // constraint única em ProcessedEvent.EventId e demais ajustes de schema completos são
        // escopo das issues #8/#12.
        modelBuilder.Entity<DailyConsolidation>(entity =>
        {
            entity.HasKey(d => d.Id);
            entity.HasIndex(d => d.Data).IsUnique();
        });

        modelBuilder.Entity<ProcessedEvent>(entity =>
        {
            entity.HasKey(e => e.EventId);
        });
    }
}
