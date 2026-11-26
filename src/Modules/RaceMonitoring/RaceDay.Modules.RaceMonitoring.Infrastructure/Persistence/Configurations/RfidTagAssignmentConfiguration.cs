using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RaceDay.Modules.RaceMonitoring.Domain;

namespace RaceDay.Modules.RaceMonitoring.Infrastructure.Persistence.Configurations;

public sealed class RfidTagAssignmentConfiguration : IEntityTypeConfiguration<RfidTagAssignment>
{
    public const string TagIndex = "IX_rfid_tag_assignments_Event_Tag";
    public const string RegistrationIndex = "IX_rfid_tag_assignments_Event_Registration";

    public void Configure(EntityTypeBuilder<RfidTagAssignment> builder)
    {
        builder.AsMonitoringRecord("rfid_tag_assignments");
        builder.Property(x => x.EventId).IsRequired();
        builder.Property(x => x.RegistrationId).IsRequired();
        builder.Property(x => x.TagScheme).HasMaxLength(16).IsRequired();
        builder.Property(x => x.TagId).HasMaxLength(128).IsRequired();
        builder.Property(x => x.AssignedByUserId).IsRequired();
        builder.Property(x => x.AssignedAtUtc).IsRequired();
        builder.HasIndex(x => new { x.EventId, x.TagScheme, x.TagId })
            .IsUnique()
            .HasDatabaseName(TagIndex);
        builder.HasIndex(x => new { x.EventId, x.RegistrationId })
            .IsUnique()
            .HasDatabaseName(RegistrationIndex);
    }
}
