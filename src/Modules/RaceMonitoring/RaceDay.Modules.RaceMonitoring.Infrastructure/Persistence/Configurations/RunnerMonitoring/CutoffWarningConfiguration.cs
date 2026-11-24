using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RaceDay.Modules.RaceMonitoring.Domain;
using RaceDay.Modules.RaceMonitoring.Domain.RunnerMonitoring;

namespace RaceDay.Modules.RaceMonitoring.Infrastructure.Persistence.Configurations.RunnerMonitoring;

public sealed class CutoffWarningConfiguration : IEntityTypeConfiguration<CutoffWarning>
{
    public const string UniqueRunnerCheckpointIndex = "IX_cutoff_warnings_RegistrationId_CheckpointId";

    public void Configure(EntityTypeBuilder<CutoffWarning> builder)
    {
        builder.AsMonitoringRecord("cutoff_warnings");
        builder.Property(x => x.EventId).IsRequired();
        builder.Property(x => x.EventName).HasMaxLength(CoordinatorFieldLengths.Name).IsRequired();
        builder.Property(x => x.DistanceId).IsRequired();
        builder.Property(x => x.DistanceName).HasMaxLength(CoordinatorFieldLengths.Name).IsRequired();
        builder.Property(x => x.RegistrationId).IsRequired();
        builder.Property(x => x.BibNumber).HasMaxLength(CoordinatorFieldLengths.BibNumber).IsRequired();
        builder.Property(x => x.RunnerName).HasMaxLength(CoordinatorFieldLengths.Name).IsRequired();
        builder.Property(x => x.CheckpointId).IsRequired();
        builder.Property(x => x.CheckpointName).HasMaxLength(CoordinatorFieldLengths.Name).IsRequired();
        builder.Property(x => x.CheckpointKm).IsRequired();
        builder.Property(x => x.CheckpointSortOrder).IsRequired();
        builder.Property(x => x.CutoffAtUtc).IsRequired();
        builder.Property(x => x.LastCheckpointName).HasMaxLength(CoordinatorFieldLengths.Name);
        builder.Property(x => x.DetectedAtUtc).IsRequired();
        builder.Property(x => x.Status).AsStoredEnum();
        builder.Property(x => x.Resolution).AsStoredEnum();
        builder.Property(x => x.DismissalNote).HasMaxLength(CutoffWarningFieldLengths.DismissalNote);
        builder.Ignore(x => x.IsActive);

        // One record per runner and checkpoint, whatever its status: dismissed warnings are not raised again.
        builder.HasIndex(x => new { x.RegistrationId, x.CheckpointId })
            .IsUnique()
            .HasDatabaseName(UniqueRunnerCheckpointIndex);
        builder.HasIndex(x => new { x.EventId, x.Status, x.DetectedAtUtc });
        builder.HasIndex(x => new { x.RegistrationId, x.Status });
    }
}
