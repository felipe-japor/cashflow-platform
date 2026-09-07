using Lancamentos.Domain;
using Microsoft.EntityFrameworkCore;

namespace Lancamentos.Infrastructure;

public class LancamentosDbContext(DbContextOptions<LancamentosDbContext> options) : DbContext(options)
{
    public DbSet<Transaction> Transactions => Set<Transaction>();
    public DbSet<OutboxEvent> OutboxEvents => Set<OutboxEvent>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Mapeamento mínimo para o esqueleto compilar e migrar (issue #6). Índices, precisão de
        // decimal e demais ajustes de schema completos são escopo da issue #8.
        modelBuilder.Entity<Transaction>(entity =>
        {
            entity.HasKey(t => t.Id);
            entity.Property(t => t.Descricao).IsRequired();
        });

        modelBuilder.Entity<OutboxEvent>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.EventType).IsRequired();
            entity.Property(e => e.Payload).IsRequired();
        });
    }
}
