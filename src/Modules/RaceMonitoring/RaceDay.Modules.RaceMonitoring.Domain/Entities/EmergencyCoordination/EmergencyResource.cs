using RaceDay.Modules.RaceMonitoring.Domain;
using RaceDay.Shared.Domain.Abstractions;

namespace RaceDay.Modules.RaceMonitoring.Domain.EmergencyCoordination;

/// <summary>
/// Operational resource such as a medical team or ambulance.
/// EventId refers to event.race_events with no cross-schema foreign key.
/// </summary>
public sealed class EmergencyResource : AggregateRoot
{
    public Guid? EventId { get; private set; }
    public string ResourceCode { get; private set; } = string.Empty;
    public EmergencyResourceType ResourceType { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public EmergencyResourceStatus Status { get; private set; }
    public string? Notes { get; private set; }

    private EmergencyResource()
    {
    }

    public static EmergencyResource Create(
        Guid? eventId,
        string resourceCode,
        EmergencyResourceType resourceType,
        string name,
        EmergencyResourceStatus status,
        string? notes = null)
    {
        CoordinatorInvariants.OptionalId(eventId, "Tham chiếu sự kiện không hợp lệ.");

        return new EmergencyResource
        {
            EventId = eventId,
            ResourceCode = CoordinatorInvariants.RequiredText(
                resourceCode,
                CoordinatorFieldLengths.Code,
                "Mã nguồn lực không được để trống.",
                "Mã nguồn lực tối đa 50 ký tự."),
            ResourceType = CoordinatorInvariants.RequiredEnum(resourceType, "Loại nguồn lực không hợp lệ."),
            Name = CoordinatorInvariants.RequiredText(
                name,
                CoordinatorFieldLengths.Name,
                "Tên nguồn lực không được để trống.",
                "Tên nguồn lực tối đa 200 ký tự."),
            Status = CoordinatorInvariants.RequiredEnum(status, "Trạng thái nguồn lực không hợp lệ."),
            Notes = CoordinatorInvariants.OptionalLongText(notes)
        };
    }
}
