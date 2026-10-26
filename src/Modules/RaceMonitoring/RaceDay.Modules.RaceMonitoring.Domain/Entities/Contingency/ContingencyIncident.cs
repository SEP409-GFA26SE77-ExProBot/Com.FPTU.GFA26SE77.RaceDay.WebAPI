using RaceDay.Modules.RaceMonitoring.Domain;
using RaceDay.Shared.Domain.Abstractions;

namespace RaceDay.Modules.RaceMonitoring.Domain.Contingency;

/// <summary>
/// Operational incident. EventId refers to event.race_events with no cross-schema foreign key.
/// </summary>
public sealed class ContingencyIncident : AggregateRoot
{
    public Guid EventId { get; private set; }
    public Guid? ContingencyPlanId { get; private set; }
    public ContingencyPlan? ContingencyPlan { get; private set; }
    public ContingencyIncidentType IncidentType { get; private set; }
    public ContingencySeverity Severity { get; private set; }
    public string Description { get; private set; } = string.Empty;
    public DateTime DetectedAtUtc { get; private set; }
    public ContingencyIncidentStatus Status { get; private set; }
    public DateTime? ResolvedAtUtc { get; private set; }

    private ContingencyIncident()
    {
    }

    public static ContingencyIncident Create(
        Guid eventId,
        ContingencyIncidentType incidentType,
        ContingencySeverity severity,
        string description,
        DateTime detectedAtUtc,
        ContingencyIncidentStatus status,
        Guid? contingencyPlanId = null,
        DateTime? resolvedAtUtc = null)
    {
        CoordinatorInvariants.RequiredId(eventId, "Tham chiếu sự kiện không hợp lệ.");
        CoordinatorInvariants.OptionalId(contingencyPlanId, "Tham chiếu kế hoạch dự phòng không hợp lệ.");

        return new ContingencyIncident
        {
            EventId = eventId,
            ContingencyPlanId = contingencyPlanId,
            IncidentType = CoordinatorInvariants.RequiredEnum(incidentType, "Loại sự cố không hợp lệ."),
            Severity = CoordinatorInvariants.RequiredEnum(severity, "Mức độ sự cố không hợp lệ."),
            Description = CoordinatorInvariants.RequiredLongText(description, "Mô tả sự cố không được để trống."),
            DetectedAtUtc = CoordinatorInvariants.RequiredTimestamp(detectedAtUtc, "Thời điểm phát hiện sự cố không hợp lệ."),
            Status = CoordinatorInvariants.RequiredEnum(status, "Trạng thái sự cố không hợp lệ."),
            ResolvedAtUtc = CoordinatorInvariants.OptionalTimestamp(resolvedAtUtc, "Thời điểm xử lý sự cố không hợp lệ.")
        };
    }
}
