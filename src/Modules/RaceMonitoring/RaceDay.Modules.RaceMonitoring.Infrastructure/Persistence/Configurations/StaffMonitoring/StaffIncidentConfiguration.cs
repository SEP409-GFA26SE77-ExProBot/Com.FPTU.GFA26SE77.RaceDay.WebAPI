using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RaceDay.Modules.RaceMonitoring.Domain;
using RaceDay.Modules.RaceMonitoring.Domain.StaffMonitoring;

namespace RaceDay.Modules.RaceMonitoring.Infrastructure.Persistence.Configurations.StaffMonitoring;

public sealed class StaffIncidentConfiguration : IEntityTypeConfiguration<StaffIncident>
{
    public const string OpenExitIndex = "IX_staff_incidents_open_exit";

    public void Configure(EntityTypeBuilder<StaffIncident> builder)
    {
        builder.AsMonitoringRecord("staff_incidents");
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.EventId).IsRequired();
        builder.Property(x => x.UserId).IsRequired();
        builder.Property(x => x.Kind).AsStoredEnum();
        builder.Property(x => x.StaffName).HasMaxLength(CoordinatorFieldLengths.Name).IsRequired();
        builder.Property(x => x.Role).HasMaxLength(StaffIncidentFieldLengths.Role).IsRequired();
        builder.Property(x => x.ZoneName).HasMaxLength(StaffIncidentFieldLengths.ZoneName);
        builder.Property(x => x.IncidentType).AsStoredEnum();
        builder.Property(x => x.Description).HasMaxLength(StaffIncidentFieldLengths.Description);
        builder.Property(x => x.PhotoUrl).HasMaxLength(StaffIncidentFieldLengths.PhotoUrl);
        builder.Property(x => x.OccurredAtUtc).IsRequired();

        builder.HasIndex(x => new { x.EventId, x.OccurredAtUtc }).IsDescending(false, true);

        // At most one open exit per staff member and event: a repeated "left the zone" is a no-op.
        builder.HasIndex(x => new { x.EventId, x.UserId })
            .IsUnique()
            .HasFilter("\"Kind\" = 'OutOfZone' AND \"ReturnedAtUtc\" IS NULL")
            .HasDatabaseName(OpenExitIndex);
    }
}
