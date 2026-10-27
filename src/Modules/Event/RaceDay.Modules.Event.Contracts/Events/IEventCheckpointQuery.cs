namespace RaceDay.Modules.Event.Contracts.Events;

/// <summary>Đọc checkpoint của cự ly. CheckpointId là id bản ghi event_checkpoints.</summary>
public interface IEventCheckpointQuery
{
    Task<EventCheckpointInfo?> GetAsync(
        Guid checkpointId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<EventCheckpointInfo>> ListByEventAsync(
        Guid eventId,
        CancellationToken cancellationToken = default);
}

public sealed record EventCheckpointInfo(
    Guid CheckpointId,
    Guid EventId,
    Guid DistanceId);
