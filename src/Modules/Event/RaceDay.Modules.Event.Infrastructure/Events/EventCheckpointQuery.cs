using Microsoft.EntityFrameworkCore;
using RaceDay.Modules.Event.Contracts.Events;
using RaceDay.Modules.Event.Infrastructure.Persistence;

namespace RaceDay.Modules.Event.Infrastructure.Events;

public sealed class EventCheckpointQuery(EventDbContext db) : IEventCheckpointQuery
{
    public async Task<EventCheckpointInfo?> GetAsync(
        Guid checkpointId,
        CancellationToken cancellationToken = default)
        => await Rows(checkpointId, null).FirstOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<EventCheckpointInfo>> ListByEventAsync(
        Guid eventId,
        CancellationToken cancellationToken = default)
        => await Rows(null, eventId).ToListAsync(cancellationToken);

    private IQueryable<EventCheckpointInfo> Rows(Guid? checkpointId, Guid? eventId)
        => from checkpoint in db.Checkpoints.AsNoTracking()
           join distance in db.Distances.AsNoTracking() on checkpoint.DistanceId equals distance.Id
           where (checkpointId == null || checkpoint.Id == checkpointId)
                 && (eventId == null || distance.EventId == eventId)
           select new EventCheckpointInfo(checkpoint.Id, distance.EventId, distance.Id);
}
