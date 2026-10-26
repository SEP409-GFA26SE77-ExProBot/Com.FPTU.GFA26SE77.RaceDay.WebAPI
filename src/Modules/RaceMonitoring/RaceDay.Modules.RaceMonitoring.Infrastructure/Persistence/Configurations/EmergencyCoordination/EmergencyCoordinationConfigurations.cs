using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RaceDay.Modules.RaceMonitoring.Domain;
using RaceDay.Modules.RaceMonitoring.Domain.EmergencyCoordination;
using RaceDay.Modules.RaceMonitoring.Infrastructure.Persistence.Configurations;

namespace RaceDay.Modules.RaceMonitoring.Infrastructure.Persistence.Configurations.EmergencyCoordination;

public sealed class EmergencyResponseConfiguration : IEntityTypeConfiguration<EmergencyResponse>
{
    public void Configure(EntityTypeBuilder<EmergencyResponse> builder)
    {
        builder.AsMonitoringRecord("emergency_responses");
        builder.Property(x => x.SourceSosAlertId).IsRequired();
        builder.Property(x => x.IncidentType).AsStoredEnum();
        builder.Property(x => x.Priority).AsStoredEnum();
        builder.Property(x => x.Status).AsStoredEnum();
        builder.Property(x => x.AssignedAtUtc).IsRequired(false);
        builder.Property(x => x.ResolvedAtUtc).IsRequired(false);
        builder.Property(x => x.ResolutionNotes).HasColumnType("text");

        builder.HasIndex(x => x.SourceSosAlertId).IsUnique();
        builder.HasIndex(x => new { x.Status, x.Priority });
    }
}

public sealed class EmergencyResourceConfiguration : IEntityTypeConfiguration<EmergencyResource>
{
    public void Configure(EntityTypeBuilder<EmergencyResource> builder)
    {
        builder.AsMonitoringRecord("emergency_resources");
        builder.Property(x => x.EventId).IsRequired(false);
        builder.Property(x => x.ResourceCode).HasMaxLength(CoordinatorFieldLengths.Code).IsRequired();
        builder.Property(x => x.ResourceType).AsStoredEnum();
        builder.Property(x => x.Name).HasMaxLength(CoordinatorFieldLengths.Name).IsRequired();
        builder.Property(x => x.Status).AsStoredEnum();
        builder.Property(x => x.Notes).HasColumnType("text");
        builder.HasIndex(x => new { x.EventId, x.ResourceCode })
            .IsUnique()
            .HasFilter("\"EventId\" IS NOT NULL");
        builder.HasIndex(x => x.ResourceCode)
            .IsUnique()
            .HasFilter("\"EventId\" IS NULL")
            .HasDatabaseName("IX_emergency_resources_ResourceCode_NoEvent");
        builder.HasIndex(x => x.Status);
    }
}

public sealed class EmergencyAssignmentConfiguration : IEntityTypeConfiguration<EmergencyAssignment>
{
    public void Configure(EntityTypeBuilder<EmergencyAssignment> builder)
    {
        builder.AsMonitoringRecord("emergency_assignments");
        builder.Property(x => x.EmergencyResponseId).IsRequired();
        builder.Property(x => x.EmergencyResourceId).IsRequired();
        builder.Property(x => x.AssignedAtUtc).IsRequired();
        builder.Property(x => x.AcceptedAtUtc).IsRequired(false);
        builder.Property(x => x.ArrivedAtUtc).IsRequired(false);
        builder.Property(x => x.CompletedAtUtc).IsRequired(false);
        builder.Property(x => x.Status).AsStoredEnum();
        builder.Property(x => x.Notes).HasColumnType("text");

        builder.HasOne(x => x.EmergencyResponse)
            .WithMany()
            .HasForeignKey(x => x.EmergencyResponseId)
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired()
            .HasConstraintName("FK_emg_assignments_responses_ResponseId");

        builder.HasOne(x => x.EmergencyResource)
            .WithMany()
            .HasForeignKey(x => x.EmergencyResourceId)
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired()
            .HasConstraintName("FK_emg_assignments_resources_ResourceId");

        builder.HasIndex(x => new { x.EmergencyResponseId, x.AssignedAtUtc });
        builder.HasIndex(x => new { x.EmergencyResourceId, x.AssignedAtUtc });
    }
}
