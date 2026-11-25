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

/// <summary>
/// <see cref="ExternalId"/> is the map-course checkpoint id; <see cref="Type"/> is the CheckpointType name
/// (Start, Finish, Water, Aid, Medical, Cutoff).
/// </summary>
public sealed record EventCheckpointInfo(
    Guid CheckpointId,
    Guid EventId,
    Guid DistanceId,
    string CheckpointName,
    string? ExternalId = null,
    string? Type = null,
    double Km = 0);
