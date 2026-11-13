using Microsoft.EntityFrameworkCore;
using RaceDay.Modules.AuditLog.Domain.AuditEntries;

namespace RaceDay.Modules.AuditLog.Infrastructure.Persistence;

public sealed class AuditDbContext(DbContextOptions<AuditDbContext> options) : DbContext(options)
{
    public DbSet<AuditEntry> AuditEntries => Set<AuditEntry>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("audit");

        modelBuilder.Entity<AuditEntry>(entity =>
        {
            entity.ToTable("audit_entries");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasColumnName("id");
            entity.Property(x => x.Action).HasColumnName("action").HasMaxLength(100).IsRequired();
            entity.Property(x => x.ActorId).HasColumnName("actor_id").HasMaxLength(100).IsRequired();
            entity.Property(x => x.ResourceType).HasColumnName("resource_type").HasMaxLength(100).IsRequired();
            entity.Property(x => x.ResourceId).HasColumnName("resource_id").HasMaxLength(100).IsRequired();
            entity.Property(x => x.PayloadJson).HasColumnName("payload_json").HasColumnType("text").IsRequired();
            entity.Property(x => x.OccurredOnUtc).HasColumnName("occurred_on_utc").IsRequired();
            entity.HasIndex(x => x.OccurredOnUtc);
            entity.HasIndex(x => x.Action);
            entity.Ignore(x => x.DomainEvents);
        });
    }
}
