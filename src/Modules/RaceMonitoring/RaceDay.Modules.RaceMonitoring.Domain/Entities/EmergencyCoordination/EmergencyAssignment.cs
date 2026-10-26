using RaceDay.Modules.RaceMonitoring.Domain;
using RaceDay.Shared.Domain.Abstractions;

namespace RaceDay.Modules.RaceMonitoring.Domain.EmergencyCoordination;

/// <summary>
/// Assignment of one resource to one emergency response. Rows are historical and are not overwritten.
/// </summary>
public sealed class EmergencyAssignment : AggregateRoot
{
    public Guid EmergencyResponseId { get; private set; }
    public EmergencyResponse? EmergencyResponse { get; private set; }
    public Guid EmergencyResourceId { get; private set; }
    public EmergencyResource? EmergencyResource { get; private set; }
    public DateTime AssignedAtUtc { get; private set; }
    public DateTime? AcceptedAtUtc { get; private set; }
    public DateTime? ArrivedAtUtc { get; private set; }
    public DateTime? CompletedAtUtc { get; private set; }
    public EmergencyAssignmentStatus Status { get; private set; }
    public string? Notes { get; private set; }

    private EmergencyAssignment()
    {
    }

    public static EmergencyAssignment Create(
        Guid emergencyResponseId,
        Guid emergencyResourceId,
        DateTime assignedAtUtc,
        EmergencyAssignmentStatus status,
        DateTime? acceptedAtUtc = null,
        DateTime? arrivedAtUtc = null,
        DateTime? completedAtUtc = null,
        string? notes = null)
    {
        CoordinatorInvariants.RequiredId(emergencyResponseId, "Tham chiếu phản hồi khẩn cấp không hợp lệ.");
        CoordinatorInvariants.RequiredId(emergencyResourceId, "Tham chiếu nguồn lực không hợp lệ.");

        return new EmergencyAssignment
        {
            EmergencyResponseId = emergencyResponseId,
            EmergencyResourceId = emergencyResourceId,
            AssignedAtUtc = CoordinatorInvariants.RequiredTimestamp(assignedAtUtc, "Thời điểm phân công không hợp lệ."),
            AcceptedAtUtc = CoordinatorInvariants.OptionalTimestamp(acceptedAtUtc, "Thời điểm nhận phân công không hợp lệ."),
            ArrivedAtUtc = CoordinatorInvariants.OptionalTimestamp(arrivedAtUtc, "Thời điểm có mặt không hợp lệ."),
            CompletedAtUtc = CoordinatorInvariants.OptionalTimestamp(completedAtUtc, "Thời điểm hoàn thành phân công không hợp lệ."),
            Status = CoordinatorInvariants.RequiredEnum(status, "Trạng thái phân công không hợp lệ."),
            Notes = CoordinatorInvariants.OptionalLongText(notes)
        };
    }
}
