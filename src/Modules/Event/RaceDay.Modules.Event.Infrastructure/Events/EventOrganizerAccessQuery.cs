using Microsoft.EntityFrameworkCore;
using RaceDay.Modules.Event.Contracts.Events;
using RaceDay.Modules.Event.Infrastructure.Persistence;
using RaceDay.Shared.Application.Time;

namespace RaceDay.Modules.Event.Infrastructure.Events;

public sealed class EventOrganizerAccessQuery(EventDbContext db) : IEventOrganizerAccessQuery
{
    public async Task<EventOrganizerAccessInfo?> GetAsync(
        Guid eventId,
        CancellationToken cancellationToken = default)
    {
        var race = await db.RaceEvents.AsNoTracking().Where(x => x.Id == eventId)
            .Select(x => new
            {
                x.Id, x.OrganizerUserId, x.Status, x.Name, x.EventDate,
                StartTime = x.StartTime ?? x.Distances.Min(distance => distance.StartAt)
            })
            .SingleOrDefaultAsync(cancellationToken);
        if (race is null)
        {
            return null;
        }

        return new EventOrganizerAccessInfo(
            race.Id,
            race.OrganizerUserId,
            race.Status.ToString(),
            race.Name,
            race.StartTime is { } startTime
                // Start times are race-local (Vietnam) wall-clock times, not UTC.
                ? new DateTimeOffset(RaceLocalTime.ToUtc(race.EventDate, startTime))
                : null);
    }
}
