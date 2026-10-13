using Microsoft.EntityFrameworkCore;
using RaceDay.Modules.Event.Contracts.Events;
using RaceDay.Modules.Event.Infrastructure.Persistence;

namespace RaceDay.Modules.Event.Infrastructure.Events;

public sealed class EventDistanceAccessQuery(EventDbContext db) : IEventDistanceAccessQuery
{
    public async Task<EventDistanceAccessInfo?> GetAsync(
        Guid eventId,
        Guid distanceId,
        CancellationToken cancellationToken = default)
    {
        var race = await db.RaceEvents.AsNoTracking()
            .Include(x => x.Distances)
            .FirstOrDefaultAsync(x => x.Id == eventId, cancellationToken);
        if (race is null)
        {
            return null;
        }

        var distance = race.Distances.FirstOrDefault(d => d.Id == distanceId);
        if (distance is null)
        {
            return null;
        }

        return new EventDistanceAccessInfo(
            race.Id,
            distance.Id,
            race.OrganizerUserId,
            distance.Name,
            distance.DistanceKm,
            race.Status.ToString());
    }
}
