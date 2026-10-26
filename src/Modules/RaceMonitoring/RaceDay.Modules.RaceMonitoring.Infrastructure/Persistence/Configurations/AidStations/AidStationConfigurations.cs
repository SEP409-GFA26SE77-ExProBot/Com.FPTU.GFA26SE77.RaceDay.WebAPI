using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RaceDay.Modules.RaceMonitoring.Domain;
using RaceDay.Modules.RaceMonitoring.Domain.AidStations;
using RaceDay.Modules.RaceMonitoring.Infrastructure.Persistence.Configurations;

namespace RaceDay.Modules.RaceMonitoring.Infrastructure.Persistence.Configurations.AidStations;

public sealed class AidStationConfiguration : IEntityTypeConfiguration<AidStation>
{
    public void Configure(EntityTypeBuilder<AidStation> builder)
    {
        builder.AsMonitoringRecord("aid_stations");
        builder.Property(x => x.EventId).IsRequired();
        builder.Property(x => x.CheckpointId).IsRequired(false);
        builder.Property(x => x.Name).HasMaxLength(CoordinatorFieldLengths.Name).IsRequired();
        builder.Property(x => x.Latitude).IsRequired();
        builder.Property(x => x.Longitude).IsRequired();
        builder.Property(x => x.Status).AsStoredEnum();
        builder.HasIndex(x => x.EventId);
        builder.HasIndex(x => x.CheckpointId);
    }
}

public sealed class SupplyItemConfiguration : IEntityTypeConfiguration<SupplyItem>
{
    public void Configure(EntityTypeBuilder<SupplyItem> builder)
    {
        builder.AsMonitoringRecord("supply_items");
        builder.Property(x => x.Name).HasMaxLength(CoordinatorFieldLengths.Name).IsRequired();
        builder.Property(x => x.Unit).HasMaxLength(CoordinatorFieldLengths.Unit).IsRequired();
        builder.Property(x => x.Description).HasColumnType("text");
        builder.Property(x => x.IsActive).IsRequired().HasDefaultValue(true);
        builder.HasIndex(x => new { x.Name, x.Unit }).IsUnique();
    }
}

public sealed class AidStationInventoryConfiguration : IEntityTypeConfiguration<AidStationInventory>
{
    public void Configure(EntityTypeBuilder<AidStationInventory> builder)
    {
        builder.AsMonitoringRecord("aid_station_inventories", table =>
        {
            table.HasCheckConstraint(
                "CK_aid_station_inventories_QuantityAvailable",
                "\"QuantityAvailable\" >= 0");
        });
        builder.Property(x => x.AidStationId).IsRequired();
        builder.Property(x => x.SupplyItemId).IsRequired();
        builder.Property(x => x.QuantityAvailable).HasPrecision(18, 3).IsRequired();
        builder.Property(x => x.LastUpdatedAtUtc).IsRequired();

        builder.HasOne(x => x.AidStation)
            .WithMany()
            .HasForeignKey(x => x.AidStationId)
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired();

        builder.HasOne(x => x.SupplyItem)
            .WithMany()
            .HasForeignKey(x => x.SupplyItemId)
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired();

        builder.HasIndex(x => new { x.AidStationId, x.SupplyItemId }).IsUnique();
    }
}

public sealed class AidStationSupplyTransactionConfiguration : IEntityTypeConfiguration<AidStationSupplyTransaction>
{
    public void Configure(EntityTypeBuilder<AidStationSupplyTransaction> builder)
    {
        builder.AsMonitoringRecord("aid_station_supply_transactions", table =>
        {
            table.HasCheckConstraint(
                "CK_aid_station_supply_transactions_Quantity",
                "\"Quantity\" > 0");
        });
        builder.Property(x => x.AidStationInventoryId).IsRequired();
        builder.Property(x => x.TransactionType).AsStoredEnum();
        builder.Property(x => x.Quantity).HasPrecision(18, 3).IsRequired();
        builder.Property(x => x.OccurredAtUtc).IsRequired();
        builder.Property(x => x.RecordedBy).IsRequired(false);
        builder.Property(x => x.Notes).HasColumnType("text");

        builder.HasOne(x => x.AidStationInventory)
            .WithMany()
            .HasForeignKey(x => x.AidStationInventoryId)
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired()
            .HasConstraintName("FK_aid_supply_tx_inventories_InventoryId");

        builder.HasIndex(x => new { x.AidStationInventoryId, x.OccurredAtUtc })
            .HasDatabaseName("IX_aid_supply_tx_InventoryId_OccurredAtUtc");
    }
}
