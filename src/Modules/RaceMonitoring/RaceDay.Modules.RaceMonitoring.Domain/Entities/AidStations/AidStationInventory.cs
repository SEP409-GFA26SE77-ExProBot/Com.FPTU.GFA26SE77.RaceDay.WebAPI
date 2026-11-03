using RaceDay.Modules.RaceMonitoring.Domain;
using RaceDay.Shared.Domain.Abstractions;
using RaceDay.Shared.Domain.Exceptions;

namespace RaceDay.Modules.RaceMonitoring.Domain.AidStations;

/// <summary>Current quantity of one supply item at one aid station. One row per station and item.</summary>
public sealed class AidStationInventory : AggregateRoot
{
    public Guid AidStationId { get; private set; }
    public AidStation? AidStation { get; private set; }
    public Guid SupplyItemId { get; private set; }
    public SupplyItem? SupplyItem { get; private set; }
    public decimal QuantityAvailable { get; private set; }
    public DateTime LastUpdatedAtUtc { get; private set; }

    private AidStationInventory()
    {
    }

    public static AidStationInventory Create(
        Guid aidStationId,
        Guid supplyItemId,
        decimal quantityAvailable,
        DateTime lastUpdatedAtUtc)
    {
        CoordinatorInvariants.RequiredId(aidStationId, "Tham chiếu trạm tiếp tế không hợp lệ.");
        CoordinatorInvariants.RequiredId(supplyItemId, "Tham chiếu vật tư không hợp lệ.");

        if (quantityAvailable < 0)
        {
            throw new DomainException("Số lượng tồn không được âm.");
        }

        return new AidStationInventory
        {
            AidStationId = aidStationId,
            SupplyItemId = supplyItemId,
            QuantityAvailable = quantityAvailable,
            LastUpdatedAtUtc = CoordinatorInvariants.RequiredTimestamp(
                lastUpdatedAtUtc,
                "Thời điểm cập nhật tồn kho không hợp lệ.")
        };
    }
}
