using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RaceDay.Modules.RaceMonitoring.Domain;

namespace RaceDay.Modules.RaceMonitoring.Infrastructure.Persistence.Configurations;

public sealed class RaceEventLogConfiguration : IEntityTypeConfiguration<RaceEventLog>
{
    public void Configure(EntityTypeBuilder<RaceEventLog> builder)
    {
        builder.AsMonitoringRecord("race_event_logs");
        builder.Property(x => x.Sequence).UseIdentityAlwaysColumn();
        builder.Property(x => x.EventId).IsRequired();
        builder.Property(x => x.EventDistanceId);
        builder.Property(x => x.EventType).AsStoredEnum();
        builder.Property(x => x.OccurredAtUtc).IsRequired();
        builder.Property(x => x.RecordedAtUtc).IsRequired();
        builder.Property(x => x.BibNumber).HasMaxLength(CoordinatorFieldLengths.BibNumber);
        builder.Property(x => x.CheckpointId);
        builder.Property(x => x.RegistrationId);
        builder.Property(x => x.SourceId);
        builder.Property(x => x.SourceType).HasMaxLength(CoordinatorFieldLengths.SourceType);
        builder.Property(x => x.Metadata).HasColumnType("jsonb");

        builder.HasIndex(x => new { x.EventId, x.Sequence })
            .HasDatabaseName("IX_race_event_logs_EventId_Sequence");
        builder.HasIndex(x => new { x.EventId, x.OccurredAtUtc, x.Sequence })
            .HasDatabaseName("IX_race_event_logs_EventId_OccurredAtUtc_Sequence");
        builder.HasIndex(x => new { x.EventId, x.EventDistanceId, x.Sequence })
            .HasDatabaseName("IX_race_event_logs_EventId_Distance_Sequence");
        builder.HasIndex(x => new { x.EventId, x.BibNumber })
            .HasFilter("\"BibNumber\" IS NOT NULL")
            .HasDatabaseName("IX_race_event_logs_EventId_BibNumber");
        builder.HasIndex(x => new { x.EventId, x.EventDistanceId })
            .IsUnique()
            .HasFilter("\"EventType\" = 'RaceStarted'")
            .HasDatabaseName("IX_race_event_logs_RaceStarted_Distance");
        builder.HasIndex(x => new { x.SourceType, x.SourceId })
            .IsUnique()
            .HasFilter("\"SourceId\" IS NOT NULL")
            .HasDatabaseName("IX_race_event_logs_Source");
    }
}
