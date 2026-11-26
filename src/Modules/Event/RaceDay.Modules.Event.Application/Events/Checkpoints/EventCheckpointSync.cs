using RaceDay.Modules.Event.Application.Abstractions;
using RaceDay.Modules.Event.Contracts.Events;
using RaceDay.Modules.Event.Domain.Events;
using RaceDay.Modules.RaceMonitoring.Contracts.Timeline;
using RaceDay.Shared.Domain.Exceptions;

namespace RaceDay.Modules.Event.Application.Events.Checkpoints;

/// <summary>
/// Called by Map when the organizer saves a course. The caller has already checked ownership.
/// A checkpoint "has data" when RaceMonitoring holds a scan or timeline row for it, or Event holds a passage.
/// </summary>
public sealed class EventCheckpointSync(
    IRaceEventRepository events,
    IRegistrationCheckpointPassageRepository passages,
    ICheckpointActivityQuery activity,
    IEventUnitOfWork unitOfWork) : IEventCheckpointSync
{
    public async Task<EventCheckpointSyncResult> SyncFromCourseAsync(
        Guid eventId,
        Guid distanceId,
        IReadOnlyList<CourseCheckpointSyncItem> checkpoints,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(checkpoints);
        var race = await events.GetByIdWithDetailsAsync(eventId, cancellationToken)
            ?? throw new DomainException("Không tìm thấy giải.");
        var distance = race.GetDistance(distanceId);

        var drafts = checkpoints
            .Select(item => new CourseCheckpointDraft(
                item.ExternalId,
                item.Name,
                item.Km,
                item.Lat,
                item.Lng,
                RaceEventDtoMapper.ParseCheckpointType(item.Type),
                item.Cutoff))
            .ToList();

        var existingIds = distance.Checkpoints.Select(checkpoint => checkpoint.Id).ToList();
        var withData = new HashSet<Guid>();
        if (existingIds.Count > 0)
        {
            withData.UnionWith(await activity.ListCheckpointIdsWithActivityAsync(existingIds, cancellationToken));
            withData.UnionWith(await passages.ListCheckpointIdsWithPassagesAsync(existingIds, cancellationToken));
        }

        var outcome = distance.SyncCheckpointsFromCourse(drafts, withData);
        events.AddCheckpoints(outcome.NewCheckpoints);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return new EventCheckpointSyncResult(outcome.Added, outcome.Updated, outcome.Removed);
    }
}
