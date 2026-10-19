using Microsoft.EntityFrameworkCore;
using RaceDay.Modules.Event.Contracts.Events;
using RaceDay.Modules.Event.Infrastructure.Persistence;

namespace RaceDay.Modules.Event.Infrastructure.Events;

public sealed class EventOrganizerAccessQuery(EventDbContext db) : IEventOrganizerAccessQuery
{
    public async Task<EventOrganizerAccessInfo?> GetAsync(
        Guid eventId,
        CancellationToken cancellationToken = default)
    {
        var race = await db.RaceEvents.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == eventId, cancellationToken);
        if (race is null)
        {
            return null;
        }

        return new EventOrganizerAccessInfo(
            race.Id,
            race.OrganizerUserId,
            race.Status.ToString(),
            race.Name);
    }
}
