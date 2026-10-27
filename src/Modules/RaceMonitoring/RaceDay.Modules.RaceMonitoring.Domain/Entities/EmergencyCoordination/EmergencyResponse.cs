using RaceDay.Modules.RaceMonitoring.Domain;
using RaceDay.Shared.Domain.Abstractions;
using RaceDay.Shared.Domain.Exceptions;

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
    public DateTime? CancelledAtUtc { get; private set; }
    public string? CancellationReason { get; private set; }

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

    /// <summary>
    /// Coordinator accepts an open response. Sets <see cref="EmergencyResponseStatus.Assigned"/>
    /// and <see cref="AssignedAtUtc"/>. Does not create an <see cref="EmergencyAssignment"/>.
    /// </summary>
    public void Acknowledge(DateTime assignedAtUtc)
    {
        if (Status != EmergencyResponseStatus.Open)
        {
            throw new DomainException("Chỉ phản hồi đang mở mới được xác nhận.");
        }

        AssignedAtUtc = CoordinatorInvariants.RequiredTimestamp(assignedAtUtc, "Thời điểm phân công khẩn cấp không hợp lệ.");
        Status = EmergencyResponseStatus.Assigned;
    }

    /// <summary>
    /// Updates priority and/or moves <see cref="EmergencyResponseStatus.Assigned"/> to
    /// <see cref="EmergencyResponseStatus.InProgress"/>.
    /// </summary>
    public void UpdateProgress(EmergencyPriority? priority, EmergencyResponseStatus? status)
    {
        if (Status is not (EmergencyResponseStatus.Assigned or EmergencyResponseStatus.InProgress))
        {
            throw new DomainException("Không thể cập nhật tiến độ ở trạng thái hiện tại.");
        }

        if (priority is null && status is null)
        {
            throw new DomainException("Cần mức ưu tiên hoặc trạng thái tiến độ.");
        }

        if (priority is EmergencyPriority nextPriority)
        {
            Priority = CoordinatorInvariants.RequiredEnum(nextPriority, "Mức ưu tiên khẩn cấp không hợp lệ.");
        }

        if (status is EmergencyResponseStatus nextStatus)
        {
            CoordinatorInvariants.RequiredEnum(nextStatus, "Trạng thái phản hồi khẩn cấp không hợp lệ.");
            if (nextStatus != EmergencyResponseStatus.InProgress)
            {
                throw new DomainException("Chỉ được chuyển tiến độ sang InProgress.");
            }

            if (Status != EmergencyResponseStatus.Assigned)
            {
                throw new DomainException("Không thể chuyển tiến độ ở trạng thái hiện tại.");
            }

            Status = EmergencyResponseStatus.InProgress;
        }
    }

    /// <summary>
    /// Completes this response from <see cref="EmergencyResponseStatus.Assigned"/> or
    /// <see cref="EmergencyResponseStatus.InProgress"/>. Does not change the source Device SOS.
    /// </summary>
    public void Resolve(string? resolutionNotes, DateTime resolvedAtUtc)
    {
        if (Status is not (EmergencyResponseStatus.Assigned or EmergencyResponseStatus.InProgress))
        {
            throw new DomainException("Phản hồi khẩn cấp không thể kết thúc ở trạng thái hiện tại.");
        }

        var notes = CoordinatorInvariants.RequiredLongText(
            resolutionNotes,
            "Nội dung kết thúc phản hồi không được để trống.");
        ResolvedAtUtc = CoordinatorInvariants.RequiredTimestamp(
            resolvedAtUtc,
            "Thời điểm kết thúc phản hồi khẩn cấp không hợp lệ.");
        ResolutionNotes = notes;
        Status = EmergencyResponseStatus.Resolved;
    }

    /// <summary>
    /// Hủy hồ sơ từ Open, Assigned hoặc InProgress. Bắt buộc có lý do và thời điểm.
    /// Không đổi SOS nguồn và không sửa phân công; handler chuyển phân công sang Completed.
    /// </summary>
    public void Cancel(string? reason, DateTime cancelledAtUtc)
    {
        if (Status is EmergencyResponseStatus.Resolved or EmergencyResponseStatus.Cancelled)
        {
            throw new DomainException("Phản hồi khẩn cấp không thể hủy ở trạng thái hiện tại.");
        }

        CancellationReason = CoordinatorInvariants.RequiredLongText(
            reason,
            "Lý do hủy phản hồi không được để trống.");
        CancelledAtUtc = CoordinatorInvariants.RequiredTimestamp(
            cancelledAtUtc,
            "Thời điểm hủy phản hồi không hợp lệ.");
        Status = EmergencyResponseStatus.Cancelled;
    }
}
