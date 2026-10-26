using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RaceDay.Modules.RaceMonitoring.Domain.RunnerMonitoring;
using RaceDay.Modules.RaceMonitoring.Infrastructure.Persistence.Configurations;

namespace RaceDay.Modules.RaceMonitoring.Infrastructure.Persistence.Configurations.RunnerMonitoring;

public sealed class RunnerStatusHistoryConfiguration : IEntityTypeConfiguration<RunnerStatusHistory>
{
    public void Configure(EntityTypeBuilder<RunnerStatusHistory> builder)
    {
        builder.AsMonitoringRecord("runner_status_histories");
        builder.Property(x => x.RegistrationId).IsRequired();
        builder.Property(x => x.PreviousStatus).AsStoredEnum();
        builder.Property(x => x.NewStatus).AsStoredEnum();
        builder.Property(x => x.ChangedAtUtc).IsRequired();
        builder.Property(x => x.ChangedBy).IsRequired(false);
        builder.Property(x => x.Reason).HasColumnType("text");
        builder.HasIndex(x => new { x.RegistrationId, x.ChangedAtUtc });
    }
}

public sealed class RouteDeviationConfiguration : IEntityTypeConfiguration<RouteDeviation>
{
    public void Configure(EntityTypeBuilder<RouteDeviation> builder)
    {
        builder.AsMonitoringRecord("route_deviations", table =>
        {
            table.HasCheckConstraint(
                "CK_route_deviations_DistanceFromRoute",
                "\"DistanceFromRoute\" >= 0");
        });
        builder.Property(x => x.RegistrationId).IsRequired();
        builder.Property(x => x.DetectedAtUtc).IsRequired();
        builder.Property(x => x.DistanceFromRoute).HasPrecision(12, 2).IsRequired();
        builder.Property(x => x.Severity).AsStoredEnum();
        builder.Property(x => x.Status).AsStoredEnum();
        builder.Property(x => x.ResolvedAtUtc).IsRequired(false);
        builder.Property(x => x.Notes).HasColumnType("text");
        builder.HasIndex(x => new { x.RegistrationId, x.DetectedAtUtc });
        builder.HasIndex(x => new { x.Status, x.DetectedAtUtc });
    }
}
