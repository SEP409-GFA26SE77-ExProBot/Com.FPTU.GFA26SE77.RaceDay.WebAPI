using RaceDay.Modules.RaceMonitoring.Domain;
using RaceDay.Shared.Domain.Abstractions;
using RaceDay.Shared.Domain.Exceptions;

namespace RaceDay.Modules.RaceMonitoring.Domain.EmergencyCoordination;

/// <summary>
/// Một lần điều một nguồn lực tới một hồ sơ. Tiến độ cập nhật trên cùng dòng và ghi mốc thời gian.
/// Gỡ khỏi hồ sơ thì xóa dòng. Mỗi cặp hồ sơ và nguồn lực chỉ có một dòng.
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

    /// <summary>
    /// Chuyển một bước: Assigned → Accepted → Arrived → Completed. Mỗi bước ghi mốc thời gian của bước đó.
    /// </summary>
    public void Advance(EmergencyAssignmentStatus nextStatus, DateTime occurredAtUtc)
    {
        var at = CoordinatorInvariants.RequiredTimestamp(occurredAtUtc, "Thời điểm cập nhật phân công không hợp lệ.");
        switch (nextStatus)
        {
            case EmergencyAssignmentStatus.Accepted when Status == EmergencyAssignmentStatus.Assigned:
                AcceptedAtUtc = at;
                Status = EmergencyAssignmentStatus.Accepted;
                return;
            case EmergencyAssignmentStatus.Arrived when Status == EmergencyAssignmentStatus.Accepted:
                ArrivedAtUtc = at;
                Status = EmergencyAssignmentStatus.Arrived;
                return;
            case EmergencyAssignmentStatus.Completed when Status == EmergencyAssignmentStatus.Arrived:
                CompletedAtUtc = at;
                Status = EmergencyAssignmentStatus.Completed;
                return;
            default:
                throw new DomainException("Không thể chuyển phân công ở trạng thái hiện tại.");
        }
    }

    /// <summary>
    /// Hồ sơ ứng cứu bị hủy: ghi nhận phân công đã kết thúc bằng Completed, không dùng Cancelled.
    /// Phân công đã Completed giữ nguyên mốc hoàn thành.
    /// </summary>
    public void CompleteBecauseResponseCancelled(DateTime completedAtUtc)
    {
        if (Status == EmergencyAssignmentStatus.Completed)
        {
            return;
        }

        CompletedAtUtc = CoordinatorInvariants.RequiredTimestamp(
            completedAtUtc,
            "Thời điểm hoàn thành phân công không hợp lệ.");
        Status = EmergencyAssignmentStatus.Completed;
    }
}
