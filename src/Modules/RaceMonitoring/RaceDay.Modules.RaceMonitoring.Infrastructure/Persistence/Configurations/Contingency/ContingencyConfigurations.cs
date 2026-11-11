using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RaceDay.Modules.RaceMonitoring.Domain;
using RaceDay.Modules.RaceMonitoring.Domain.Contingency;
using RaceDay.Modules.RaceMonitoring.Infrastructure.Persistence.Configurations;

namespace RaceDay.Modules.RaceMonitoring.Infrastructure.Persistence.Configurations.Contingency;

public sealed class ContingencyPlanConfiguration : IEntityTypeConfiguration<ContingencyPlan>
{
    public void Configure(EntityTypeBuilder<ContingencyPlan> builder)
    {
        builder.AsMonitoringRecord("contingency_plans");
        builder.Property(x => x.EventId).IsRequired();
        builder.Property(x => x.PlanType).AsStoredEnum();
        builder.Property(x => x.Name).HasMaxLength(CoordinatorFieldLengths.Name).IsRequired();
        builder.Property(x => x.Description).HasColumnType("text").IsRequired();
        builder.Property(x => x.TriggerCondition).HasColumnType("text");
        builder.Property(x => x.Status).AsStoredEnum();
        builder.Property(x => x.CreatedAtUtc).IsRequired();
        builder.Property(x => x.CreatedBy).IsRequired(false);
        builder.HasIndex(x => new { x.EventId, x.Status });
    }
}

public sealed class ContingencyIncidentConfiguration : IEntityTypeConfiguration<ContingencyIncident>
{
    public void Configure(EntityTypeBuilder<ContingencyIncident> builder)
    {
        builder.AsMonitoringRecord("contingency_incidents");
        builder.Property(x => x.EventId).IsRequired();
        builder.Property(x => x.ContingencyPlanId).IsRequired(false);
        builder.Property(x => x.IncidentType).AsStoredEnum();
        builder.Property(x => x.Severity).AsStoredEnum();
        builder.Property(x => x.Description).HasColumnType("text").IsRequired();
        builder.Property(x => x.DetectedAtUtc).IsRequired();
        builder.Property(x => x.Status).AsStoredEnum();
        builder.Property(x => x.ResolvedAtUtc).IsRequired(false);

        builder.HasOne(x => x.ContingencyPlan)
            .WithMany()
            .HasForeignKey(x => x.ContingencyPlanId)
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired(false);

        builder.HasIndex(x => new { x.EventId, x.DetectedAtUtc });
        builder.HasIndex(x => x.ContingencyPlanId);
    }
}

public sealed class EmergencyDecisionConfiguration : IEntityTypeConfiguration<EmergencyDecision>
{
    public void Configure(EntityTypeBuilder<EmergencyDecision> builder)
    {
        builder.AsMonitoringRecord("emergency_decisions");
        builder.Property(x => x.ContingencyIncidentId).IsRequired();
        builder.Property(x => x.DecisionType).AsStoredEnum();
        builder.Property(x => x.DecisionDetails).HasColumnType("text").IsRequired();
        builder.Property(x => x.DecidedBy).IsRequired();
        builder.Property(x => x.DecidedAtUtc).IsRequired();
        builder.Property(x => x.EffectiveAtUtc).IsRequired(false);

        builder.HasOne(x => x.ContingencyIncident)
            .WithMany()
            .HasForeignKey(x => x.ContingencyIncidentId)
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired()
            .HasConstraintName("FK_emg_decisions_incidents_IncidentId");

        builder.HasIndex(x => new { x.ContingencyIncidentId, x.DecidedAtUtc });
    }
}
