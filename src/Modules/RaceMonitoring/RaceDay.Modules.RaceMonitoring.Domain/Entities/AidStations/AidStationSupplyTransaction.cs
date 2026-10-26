using RaceDay.Modules.RaceMonitoring.Domain;
using RaceDay.Shared.Domain.Abstractions;
using RaceDay.Shared.Domain.Exceptions;

namespace RaceDay.Modules.RaceMonitoring.Domain.AidStations;

/// <summary>
/// Recorded inventory movement. Quantity is a positive amount; this type does not adjust stock.
/// RecordedBy refers to iam.users with no cross-schema foreign key.
/// </summary>
public sealed class AidStationSupplyTransaction : AggregateRoot
{
    public Guid AidStationInventoryId { get; private set; }
    public AidStationInventory? AidStationInventory { get; private set; }
    public SupplyTransactionType TransactionType { get; private set; }
    public decimal Quantity { get; private set; }
    public DateTime OccurredAtUtc { get; private set; }
    public Guid? RecordedBy { get; private set; }
    public string? Notes { get; private set; }

    private AidStationSupplyTransaction()
    {
    }

    public static AidStationSupplyTransaction Create(
        Guid aidStationInventoryId,
        SupplyTransactionType transactionType,
        decimal quantity,
        DateTime occurredAtUtc,
        Guid? recordedBy = null,
        string? notes = null)
    {
        CoordinatorInvariants.RequiredId(aidStationInventoryId, "Tham chiếu tồn kho không hợp lệ.");
        CoordinatorInvariants.OptionalId(recordedBy, "Tham chiếu người dùng không hợp lệ.");

        if (quantity <= 0)
        {
            throw new DomainException("Số lượng giao dịch vật tư phải lớn hơn 0.");
        }

        return new AidStationSupplyTransaction
        {
            AidStationInventoryId = aidStationInventoryId,
            TransactionType = CoordinatorInvariants.RequiredEnum(transactionType, "Loại giao dịch vật tư không hợp lệ."),
            Quantity = quantity,
            OccurredAtUtc = CoordinatorInvariants.RequiredTimestamp(occurredAtUtc, "Thời điểm giao dịch vật tư không hợp lệ."),
            RecordedBy = recordedBy,
            Notes = CoordinatorInvariants.OptionalLongText(notes)
        };
    }
}
