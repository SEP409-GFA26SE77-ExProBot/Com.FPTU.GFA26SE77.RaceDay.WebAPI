using RaceDay.Shared.Domain.Abstractions;
using RaceDay.Shared.Domain.Exceptions;

namespace RaceDay.Modules.RaceMonitoring.Domain;

public static class RaceEventLogSource
{
    public const string CheckpointScan = "CheckpointScan";
}

/// <summary>
/// Dòng timeline append-only của một giải. Không cập nhật và không xóa.
/// Passage hợp lệ là bản ghi này, không phải entity passage của module Event.
/// </summary>
public sealed class RaceEventLog : AggregateRoot
{
    public long Sequence { get; private set; }
    public Guid EventId { get; private set; }
    public Guid? EventDistanceId { get; private set; }
    public RaceEventLogType EventType { get; private set; }
    public DateTime OccurredAtUtc { get; private set; }
    public DateTime RecordedAtUtc { get; private set; }
    public string? BibNumber { get; private set; }
    public Guid? CheckpointId { get; private set; }
    public Guid? RegistrationId { get; private set; }
    public Guid? SourceId { get; private set; }
    public string? SourceType { get; private set; }
    public string? Metadata { get; private set; }

    private RaceEventLog()
    {
    }

    public static RaceEventLog RecordCheckpointPassed(
        Guid eventId,
        Guid eventDistanceId,
        DateTime occurredAtUtc,
        DateTime recordedAtUtc,
        string bibNumber,
        Guid checkpointId,
        Guid registrationId,
        Guid sourceScanId,
        string? metadata)
    {
        CoordinatorInvariants.RequiredId(eventId, "Tham chiếu giải không hợp lệ.");
        CoordinatorInvariants.RequiredId(eventDistanceId, "Tham chiếu cự ly không hợp lệ.");
        CoordinatorInvariants.RequiredId(checkpointId, "Tham chiếu checkpoint không hợp lệ.");
        CoordinatorInvariants.RequiredId(registrationId, "Tham chiếu đăng ký không hợp lệ.");
        CoordinatorInvariants.RequiredId(sourceScanId, "Tham chiếu lượt quét không hợp lệ.");

        return new RaceEventLog
        {
            EventId = eventId,
            EventDistanceId = eventDistanceId,
            EventType = RaceEventLogType.RunnerCheckpointPassed,
            OccurredAtUtc = CoordinatorInvariants.RequiredTimestamp(occurredAtUtc, "Thời điểm qua checkpoint không hợp lệ."),
            RecordedAtUtc = CoordinatorInvariants.RequiredTimestamp(recordedAtUtc, "Thời điểm ghi log không hợp lệ."),
            BibNumber = CoordinatorInvariants.RequiredText(
                bibNumber,
                CoordinatorFieldLengths.BibNumber,
                "Thiếu số BIB.",
                "Số BIB vượt quá độ dài cho phép."),
            CheckpointId = checkpointId,
            RegistrationId = registrationId,
            SourceId = sourceScanId,
            SourceType = RaceEventLogSource.CheckpointScan,
            Metadata = NormalizeMetadata(metadata)
        };
    }

    private static string? NormalizeMetadata(string? metadata)
    {
        if (string.IsNullOrWhiteSpace(metadata))
        {
            return null;
        }

        var trimmed = metadata.Trim();
        if (trimmed.Length > CoordinatorFieldLengths.Metadata)
        {
            throw new DomainException("Metadata vượt quá độ dài cho phép.");
        }

        return trimmed;
    }
}
