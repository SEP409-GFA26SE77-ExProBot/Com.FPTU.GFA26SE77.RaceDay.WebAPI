using RaceDay.Modules.RaceMonitoring.Domain;
using RaceDay.Shared.Domain.Abstractions;

namespace RaceDay.Modules.RaceMonitoring.Domain.BroomWagons;

/// <summary>
/// Checkpoint visit recorded for a broom wagon. CheckpointId refers to event.event_checkpoints and
/// RecordedBy refers to iam.users. Neither identifier has a cross-schema foreign key.
/// A visit does not mark runners as DNF.
/// </summary>
public sealed class BroomWagonCheckpointLog : AggregateRoot
{
    public Guid BroomWagonId { get; private set; }
    public BroomWagon? BroomWagon { get; private set; }
    public Guid CheckpointId { get; private set; }
    public DateTime RecordedAtUtc { get; private set; }
    public ScanMethod ScanMethod { get; private set; }
    public Guid? RecordedBy { get; private set; }
    public string? Notes { get; private set; }

    private BroomWagonCheckpointLog()
    {
    }

    public static BroomWagonCheckpointLog Create(
        Guid broomWagonId,
        Guid checkpointId,
        DateTime recordedAtUtc,
        ScanMethod scanMethod,
        Guid? recordedBy = null,
        string? notes = null)
    {
        CoordinatorInvariants.RequiredId(broomWagonId, "Tham chiếu xe gom không hợp lệ.");
        CoordinatorInvariants.RequiredId(checkpointId, "Tham chiếu checkpoint không hợp lệ.");
        CoordinatorInvariants.OptionalId(recordedBy, "Tham chiếu người dùng không hợp lệ.");

        return new BroomWagonCheckpointLog
        {
            BroomWagonId = broomWagonId,
            CheckpointId = checkpointId,
            RecordedAtUtc = CoordinatorInvariants.RequiredTimestamp(recordedAtUtc, "Thời điểm ghi nhận xe gom không hợp lệ."),
            ScanMethod = CoordinatorInvariants.RequiredEnum(scanMethod, "Phương thức quét không hợp lệ."),
            RecordedBy = recordedBy,
            Notes = CoordinatorInvariants.OptionalLongText(notes)
        };
    }
}
