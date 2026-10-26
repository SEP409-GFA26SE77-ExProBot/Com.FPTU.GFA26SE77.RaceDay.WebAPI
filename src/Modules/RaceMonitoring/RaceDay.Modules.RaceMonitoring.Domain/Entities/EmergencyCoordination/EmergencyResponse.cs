using RaceDay.Modules.RaceMonitoring.Domain;
using RaceDay.Shared.Domain.Abstractions;

namespace RaceDay.Modules.RaceMonitoring.Domain.EmergencyCoordination;

/// <summary>
/// One operational response for a Device SOS signal.
/// <see cref="SourceSosAlertId"/> is unique: each source signal has at most one response.
/// The value is an external identifier, not a navigation or database foreign key.
/// Multiple resources and assignment history belong on <see cref="EmergencyAssignment"/>.
/// </summary>
public sealed class EmergencyResponse : AggregateRoot
{
    public Guid SourceSosAlertId { get; private set; }
    public EmergencyIncidentType IncidentType { get; private set; }
    public EmergencyPriority Priority { get; private set; }
    public EmergencyResponseStatus Status { get; private set; }
    public DateTime? AssignedAtUtc { get; private set; }
    public DateTime? ResolvedAtUtc { get; private set; }
    public string? ResolutionNotes { get; private set; }

    private EmergencyResponse()
    {
    }

    public static EmergencyResponse Create(
        Guid sourceSosAlertId,
        EmergencyIncidentType incidentType,
        EmergencyPriority priority,
        EmergencyResponseStatus status,
        DateTime? assignedAtUtc = null,
        DateTime? resolvedAtUtc = null,
        string? resolutionNotes = null)
    {
        CoordinatorInvariants.RequiredId(sourceSosAlertId, "Tham chiếu SOS không hợp lệ.");

        return new EmergencyResponse
        {
            SourceSosAlertId = sourceSosAlertId,
            IncidentType = CoordinatorInvariants.RequiredEnum(incidentType, "Loại sự cố khẩn cấp không hợp lệ."),
            Priority = CoordinatorInvariants.RequiredEnum(priority, "Mức ưu tiên khẩn cấp không hợp lệ."),
            Status = CoordinatorInvariants.RequiredEnum(status, "Trạng thái phản hồi khẩn cấp không hợp lệ."),
            AssignedAtUtc = CoordinatorInvariants.OptionalTimestamp(assignedAtUtc, "Thời điểm phân công khẩn cấp không hợp lệ."),
            ResolvedAtUtc = CoordinatorInvariants.OptionalTimestamp(resolvedAtUtc, "Thời điểm kết thúc phản hồi khẩn cấp không hợp lệ."),
            ResolutionNotes = CoordinatorInvariants.OptionalLongText(resolutionNotes)
        };
    }
}
