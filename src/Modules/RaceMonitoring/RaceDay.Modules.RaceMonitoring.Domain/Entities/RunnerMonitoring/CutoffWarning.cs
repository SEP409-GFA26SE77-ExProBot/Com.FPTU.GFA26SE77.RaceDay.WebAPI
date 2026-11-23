using RaceDay.Shared.Domain.Abstractions;
using RaceDay.Shared.Domain.Exceptions;

namespace RaceDay.Modules.RaceMonitoring.Domain.RunnerMonitoring;

/// <summary>
/// A runner did not pass a checkpoint before its cut-off. One record per registration and checkpoint.
/// Never deleted: dismissing or resolving only changes Status, so history stays for post-event review.
/// Names, BIB and last passage are snapshots taken at detection time; ids refer to Event and
/// race_monitoring.checkpoint_scans with no cross-schema foreign keys.
/// </summary>
public sealed class CutoffWarning : AggregateRoot
{
    public Guid EventId { get; private set; }
    public string EventName { get; private set; } = string.Empty;
    public Guid DistanceId { get; private set; }
    public string DistanceName { get; private set; } = string.Empty;
    public Guid RegistrationId { get; private set; }
    public string BibNumber { get; private set; } = string.Empty;
    public string RunnerName { get; private set; } = string.Empty;
    public Guid CheckpointId { get; private set; }
    public string CheckpointName { get; private set; } = string.Empty;
    public double CheckpointKm { get; private set; }
    public int CheckpointSortOrder { get; private set; }
    public DateTime CutoffAtUtc { get; private set; }
    public Guid? LastCheckpointId { get; private set; }
    public string? LastCheckpointName { get; private set; }
    public DateTime? LastPassedAtUtc { get; private set; }
    public DateTime DetectedAtUtc { get; private set; }
    public CutoffWarningStatus Status { get; private set; }
    public DateTime? ResolvedAtUtc { get; private set; }
    public CutoffWarningResolution? Resolution { get; private set; }
    public Guid? ResolvedByScanId { get; private set; }
    public DateTime? DismissedAtUtc { get; private set; }
    public Guid? DismissedByUserId { get; private set; }
    public string? DismissalNote { get; private set; }

    public bool IsActive => Status == CutoffWarningStatus.Active;

    private CutoffWarning()
    {
    }

    public static CutoffWarning Raise(
        CutoffWarningTarget target,
        DateTime cutoffAtUtc,
        CutoffWarningLastPassage? lastPassage,
        DateTime detectedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(target);
        CoordinatorInvariants.RequiredId(target.EventId, "Tham chiếu giải không hợp lệ.");
        CoordinatorInvariants.RequiredId(target.DistanceId, "Tham chiếu cự ly không hợp lệ.");
        CoordinatorInvariants.RequiredId(target.RegistrationId, "Tham chiếu đăng ký không hợp lệ.");
        CoordinatorInvariants.RequiredId(target.CheckpointId, "Tham chiếu checkpoint không hợp lệ.");
        CoordinatorInvariants.RequiredTimestamp(cutoffAtUtc, "Thời điểm cut-off không hợp lệ.");
        CoordinatorInvariants.RequiredTimestamp(detectedAtUtc, "Thời điểm phát hiện không hợp lệ.");
        if (detectedAtUtc < cutoffAtUtc)
        {
            throw new DomainException("Chỉ cảnh báo sau khi đã qua giờ cut-off.");
        }

        if (lastPassage is not null)
        {
            CoordinatorInvariants.RequiredId(lastPassage.CheckpointId, "Tham chiếu checkpoint cuối không hợp lệ.");
            CoordinatorInvariants.RequiredTimestamp(lastPassage.PassedAtUtc, "Thời điểm qua checkpoint cuối không hợp lệ.");
            if (lastPassage.CheckpointId == target.CheckpointId)
            {
                throw new DomainException("VĐV đã qua checkpoint này nên không bị cảnh báo.");
            }
        }

        return new CutoffWarning
        {
            EventId = target.EventId,
            EventName = Name(target.EventName, "Thiếu tên giải."),
            DistanceId = target.DistanceId,
            DistanceName = Name(target.DistanceName, "Thiếu tên cự ly."),
            RegistrationId = target.RegistrationId,
            BibNumber = CoordinatorInvariants.RequiredText(
                target.BibNumber,
                CoordinatorFieldLengths.BibNumber,
                "Thiếu số BIB.",
                "Số BIB vượt quá độ dài cho phép."),
            RunnerName = Name(target.RunnerName, "Thiếu tên VĐV."),
            CheckpointId = target.CheckpointId,
            CheckpointName = Name(target.CheckpointName, "Thiếu tên checkpoint."),
            CheckpointKm = target.CheckpointKm,
            CheckpointSortOrder = target.CheckpointSortOrder,
            CutoffAtUtc = cutoffAtUtc,
            LastCheckpointId = lastPassage?.CheckpointId,
            LastCheckpointName = lastPassage is null
                ? null
                : Name(lastPassage.CheckpointName, "Thiếu tên checkpoint cuối."),
            LastPassedAtUtc = lastPassage?.PassedAtUtc,
            DetectedAtUtc = detectedAtUtc,
            Status = CutoffWarningStatus.Active
        };
    }

    /// <summary>Runner was located by the system. No-op when the warning is no longer active.</summary>
    public bool Resolve(CutoffWarningResolution resolution, DateTime resolvedAtUtc, Guid? scanId = null)
    {
        CoordinatorInvariants.RequiredEnum(resolution, "Lý do đóng cảnh báo không hợp lệ.");
        CoordinatorInvariants.RequiredTimestamp(resolvedAtUtc, "Thời điểm đóng cảnh báo không hợp lệ.");
        CoordinatorInvariants.OptionalId(scanId, "Tham chiếu lượt quét không hợp lệ.");
        if (!IsActive)
        {
            return false;
        }

        Status = CutoffWarningStatus.Resolved;
        Resolution = resolution;
        ResolvedAtUtc = resolvedAtUtc;
        ResolvedByScanId = scanId;
        return true;
    }

    /// <summary>Staff verified the situation. Does not touch registration, scans or race history.</summary>
    public void Dismiss(Guid userId, string? note, DateTime dismissedAtUtc)
    {
        CoordinatorInvariants.RequiredId(userId, "Thiếu người xử lý cảnh báo.");
        CoordinatorInvariants.RequiredTimestamp(dismissedAtUtc, "Thời điểm xử lý cảnh báo không hợp lệ.");
        if (!IsActive)
        {
            throw new DomainException("Cảnh báo đã được xử lý.");
        }

        Status = CutoffWarningStatus.Dismissed;
        DismissedAtUtc = dismissedAtUtc;
        DismissedByUserId = userId;
        DismissalNote = CoordinatorInvariants.OptionalText(
            note,
            CutoffWarningFieldLengths.DismissalNote,
            "Ghi chú vượt quá độ dài cho phép.");
    }

    private static string Name(string? value, string emptyMessage)
        => CoordinatorInvariants.RequiredText(
            value,
            CoordinatorFieldLengths.Name,
            emptyMessage,
            "Tên vượt quá độ dài cho phép.");
}

public sealed record CutoffWarningTarget(
    Guid EventId,
    string EventName,
    Guid DistanceId,
    string DistanceName,
    Guid RegistrationId,
    string BibNumber,
    string RunnerName,
    Guid CheckpointId,
    string CheckpointName,
    double CheckpointKm,
    int CheckpointSortOrder);

public sealed record CutoffWarningLastPassage(
    Guid CheckpointId,
    string CheckpointName,
    DateTime PassedAtUtc);

public static class CutoffWarningFieldLengths
{
    public const int DismissalNote = 500;
}
