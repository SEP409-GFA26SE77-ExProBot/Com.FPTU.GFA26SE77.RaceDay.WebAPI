namespace RaceDay.Modules.Event.Contracts.Events;

/// <summary>
/// Keeps event_checkpoints in line with the organizer's saved map course.
/// Matches by map checkpoint id (ExternalId), adds new ones and updates name, km, position, type and cut-off.
/// Refuses (DomainException) to delete a checkpoint that already has race data.
/// </summary>
public interface IEventCheckpointSync
{
    Task<EventCheckpointSyncResult> SyncFromCourseAsync(
        Guid eventId,
        Guid distanceId,
        IReadOnlyList<CourseCheckpointSyncItem> checkpoints,
        CancellationToken cancellationToken = default);
}

/// <summary>Type is the map value (start, cp, aid, water, medical, cutoff, finish). Cutoff null keeps the stored value.</summary>
public sealed record CourseCheckpointSyncItem(
    string ExternalId,
    string Name,
    double Km,
    double Lat,
    double Lng,
    string Type,
    string? Cutoff);

public sealed record EventCheckpointSyncResult(int Added, int Updated, int Removed);
