using Microsoft.EntityFrameworkCore;
using RaceDay.Modules.RaceMonitoring.Domain;

namespace RaceDay.Modules.RaceMonitoring.Infrastructure.Persistence;

public sealed class RaceMonitoringDbContext(DbContextOptions<RaceMonitoringDbContext> options) : DbContext(options)
{
    public DbSet<RunnerLocation> RunnerLocations => Set<RunnerLocation>();
    public DbSet<CheckpointScan> CheckpointScans => Set<CheckpointScan>();
    public DbSet<SOSAlert> SOSAlerts => Set<SOSAlert>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("race_monitoring");

        modelBuilder.Entity<RunnerLocation>(entity =>
        {
            entity.ToTable("runner_locations");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.RegistrationId).IsRequired();
            entity.Property(x => x.Latitude).IsRequired();
            entity.Property(x => x.Longitude).IsRequired();
            entity.Property(x => x.RecordedAtUtc).IsRequired();
            entity.Property(x => x.ReceivedAtUtc).IsRequired();
            entity.HasIndex(x => new { x.RegistrationId, x.RecordedAtUtc });
            entity.Ignore(x => x.DomainEvents);
        });

        modelBuilder.Entity<CheckpointScan>(entity =>
        {
            entity.ToTable("checkpoint_scans");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.RegistrationId).IsRequired();
            entity.Property(x => x.CheckpointId).IsRequired();
            entity.Property(x => x.ScannedAtUtc).IsRequired();
            entity.Property(x => x.ScanMethod).HasConversion<string>().HasMaxLength(20).IsRequired();
            entity.HasIndex(x => new { x.CheckpointId, x.ScannedAtUtc });
            entity.Ignore(x => x.DomainEvents);
        });

        modelBuilder.Entity<SOSAlert>(entity =>
        {
            entity.ToTable("sos_alerts");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.RegistrationId).IsRequired();
            entity.Property(x => x.RunnerLocationId).IsRequired(false);
            entity.Property(x => x.Latitude).IsRequired(false);
            entity.Property(x => x.Longitude).IsRequired(false);
            entity.Property(x => x.TriggeredAtUtc).IsRequired();
            entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
            entity.Property(x => x.ResolvedAtUtc).IsRequired(false);
            entity.HasIndex(x => new { x.Status, x.TriggeredAtUtc });

            entity.HasOne(x => x.RunnerLocation)
                .WithMany()
                .HasForeignKey(x => x.RunnerLocationId)
                .IsRequired(false)
                .OnDelete(DeleteBehavior.Restrict);

            entity.Ignore(x => x.DomainEvents);
        });
    }
}
