using Microsoft.EntityFrameworkCore;
using RaceDay.Modules.Event.Contracts.Finance;
using RaceDay.Modules.Event.Infrastructure.Persistence;

namespace RaceDay.Modules.Event.Infrastructure.Finance;

public sealed class OrganizerRegistrationScopeQuery(EventDbContext db) : IOrganizerRegistrationScopeQuery
{
    public async Task<IReadOnlyList<Guid>> ListIdsAsync(Guid organizerId, Guid? eventId, CancellationToken ct)
        => await (from r in db.Registrations.AsNoTracking()
                  join e in db.RaceEvents on r.EventId equals e.Id
                  where e.OrganizerUserId == organizerId && (!eventId.HasValue || e.Id == eventId.Value)
                  select r.Id).ToListAsync(ct);
}
