using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RaceDay.Modules.RaceMonitoring.Domain;
using RaceDay.Modules.RaceMonitoring.Domain.BroomWagons;
using RaceDay.Modules.RaceMonitoring.Infrastructure.Persistence.Configurations;

namespace RaceDay.Modules.RaceMonitoring.Infrastructure.Persistence.Configurations.BroomWagons;

public sealed class BroomWagonConfiguration : IEntityTypeConfiguration<BroomWagon>
{
    public void Configure(EntityTypeBuilder<BroomWagon> builder)
    {
        builder.AsMonitoringRecord("broom_wagons");
        builder.Property(x => x.EventId).IsRequired();
        builder.Property(x => x.VehicleCode).HasMaxLength(CoordinatorFieldLengths.Code).IsRequired();
        builder.Property(x => x.VehicleType).HasMaxLength(CoordinatorFieldLengths.VehicleType);
        builder.Property(x => x.Status).AsStoredEnum();
        builder.Property(x => x.Notes).HasColumnType("text");
        builder.HasIndex(x => new { x.EventId, x.VehicleCode }).IsUnique();
    }
}

public sealed class BroomWagonCheckpointLogConfiguration : IEntityTypeConfiguration<BroomWagonCheckpointLog>
{
    public void Configure(EntityTypeBuilder<BroomWagonCheckpointLog> builder)
    {
        builder.AsMonitoringRecord("broom_wagon_checkpoint_logs");
        builder.Property(x => x.BroomWagonId).IsRequired();
        builder.Property(x => x.CheckpointId).IsRequired();
        builder.Property(x => x.RecordedAtUtc).IsRequired();
        builder.Property(x => x.ScanMethod).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(x => x.RecordedBy).IsRequired(false);
        builder.Property(x => x.Notes).HasColumnType("text");

        builder.HasOne(x => x.BroomWagon)
            .WithMany()
            .HasForeignKey(x => x.BroomWagonId)
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired();

        builder.HasIndex(x => new { x.BroomWagonId, x.RecordedAtUtc });
        builder.HasIndex(x => new { x.CheckpointId, x.RecordedAtUtc });
    }
}
