using Microsoft.EntityFrameworkCore;
using RaceDay.Modules.Event.Contracts.Events;
using RaceDay.Modules.Event.Domain.Staff;
using RaceDay.Modules.Event.Infrastructure.Persistence;

namespace RaceDay.Modules.Event.Infrastructure.Events;

public sealed class EventSosOpsAccessQuery(EventDbContext db) : IEventSosOpsAccessQuery
{
    public async Task<bool> CanManageSosAsync(
        Guid eventId,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var isOrganizer = await db.RaceEvents.AsNoTracking()
            .AnyAsync(x => x.Id == eventId && x.OrganizerUserId == userId, cancellationToken);
        if (isOrganizer)
        {
            return true;
        }

        return await db.StaffMembers.AsNoTracking().AnyAsync(
            x => x.EventId == eventId
                 && x.UserId == userId
                 && x.Role == EventStaffRole.Coordinator
                 && x.Status == EventStaffStatus.Active,
            cancellationToken);
    }
}
