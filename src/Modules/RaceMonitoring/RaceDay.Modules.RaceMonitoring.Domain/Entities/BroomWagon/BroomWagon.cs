using RaceDay.Modules.RaceMonitoring.Domain;
using RaceDay.Shared.Domain.Abstractions;

namespace RaceDay.Modules.RaceMonitoring.Domain.BroomWagons;

/// <summary>
/// Broom wagon assigned to an event. EventId refers to event.race_events with no cross-schema foreign key.
/// </summary>
public sealed class BroomWagon : AggregateRoot
{
    public Guid EventId { get; private set; }
    public string VehicleCode { get; private set; } = string.Empty;
    public string? VehicleType { get; private set; }
    public BroomWagonStatus Status { get; private set; }
    public string? Notes { get; private set; }

    private BroomWagon()
    {
    }

    public static BroomWagon Create(
        Guid eventId,
        string vehicleCode,
        BroomWagonStatus status,
        string? vehicleType = null,
        string? notes = null)
    {
        CoordinatorInvariants.RequiredId(eventId, "Tham chiếu sự kiện không hợp lệ.");

        return new BroomWagon
        {
            EventId = eventId,
            VehicleCode = CoordinatorInvariants.RequiredText(
                vehicleCode,
                CoordinatorFieldLengths.Code,
                "Mã xe gom không được để trống.",
                "Mã xe gom tối đa 50 ký tự."),
            VehicleType = CoordinatorInvariants.OptionalText(
                vehicleType,
                CoordinatorFieldLengths.VehicleType,
                "Loại xe gom tối đa 50 ký tự."),
            Status = CoordinatorInvariants.RequiredEnum(status, "Trạng thái xe gom không hợp lệ."),
            Notes = CoordinatorInvariants.OptionalLongText(notes)
        };
    }
}
