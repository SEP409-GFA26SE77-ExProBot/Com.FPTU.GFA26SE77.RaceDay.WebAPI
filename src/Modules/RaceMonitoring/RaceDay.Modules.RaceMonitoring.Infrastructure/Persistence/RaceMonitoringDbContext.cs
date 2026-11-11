using Microsoft.EntityFrameworkCore;
using RaceDay.Modules.RaceMonitoring.Domain;
using RaceDay.Modules.RaceMonitoring.Domain.AidStations;
using RaceDay.Modules.RaceMonitoring.Domain.BroomWagons;
using RaceDay.Modules.RaceMonitoring.Domain.Contingency;
using RaceDay.Modules.RaceMonitoring.Domain.EmergencyCoordination;
using RaceDay.Modules.RaceMonitoring.Domain.RunnerMonitoring;

namespace RaceDay.Modules.RaceMonitoring.Infrastructure.Persistence;

public sealed class RaceMonitoringDbContext(DbContextOptions<RaceMonitoringDbContext> options) : DbContext(options)
{
    public DbSet<RunnerLocation> RunnerLocations => Set<RunnerLocation>();
    public DbSet<CheckpointScan> CheckpointScans => Set<CheckpointScan>();
    public DbSet<RaceEventLog> RaceEventLogs => Set<RaceEventLog>();
    public DbSet<RunnerStatusHistory> RunnerStatusHistories => Set<RunnerStatusHistory>();
    public DbSet<RouteDeviation> RouteDeviations => Set<RouteDeviation>();
    public DbSet<EmergencyResponse> EmergencyResponses => Set<EmergencyResponse>();
    public DbSet<EmergencyResource> EmergencyResources => Set<EmergencyResource>();
    public DbSet<EmergencyAssignment> EmergencyAssignments => Set<EmergencyAssignment>();
    public DbSet<AidStation> AidStations => Set<AidStation>();
    public DbSet<SupplyItem> SupplyItems => Set<SupplyItem>();
    public DbSet<AidStationInventory> AidStationInventories => Set<AidStationInventory>();
    public DbSet<AidStationSupplyTransaction> AidStationSupplyTransactions => Set<AidStationSupplyTransaction>();
    public DbSet<ContingencyPlan> ContingencyPlans => Set<ContingencyPlan>();
    public DbSet<ContingencyIncident> ContingencyIncidents => Set<ContingencyIncident>();
    public DbSet<EmergencyDecision> EmergencyDecisions => Set<EmergencyDecision>();
    public DbSet<BroomWagon> BroomWagons => Set<BroomWagon>();
    public DbSet<BroomWagonCheckpointLog> BroomWagonCheckpointLogs => Set<BroomWagonCheckpointLog>();

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

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(RaceMonitoringDbContext).Assembly);
    }
}
