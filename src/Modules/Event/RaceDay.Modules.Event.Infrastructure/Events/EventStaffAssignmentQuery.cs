using Microsoft.EntityFrameworkCore;
using RaceDay.Modules.Event.Contracts.Events;
using RaceDay.Modules.Event.Domain.Staff;
using RaceDay.Modules.Event.Infrastructure.Persistence;

namespace RaceDay.Modules.Event.Infrastructure.Events;

public sealed class EventStaffAssignmentQuery(EventDbContext db) : IEventStaffAssignmentQuery
{
    public async Task<IReadOnlyList<EventStaffAssignment>> ListAsync(Guid eventId, Guid userId, CancellationToken cancellationToken = default)
        => await (from member in db.StaffMembers.AsNoTracking()
                  join race in db.RaceEvents.AsNoTracking() on member.EventId equals race.Id
                  where member.EventId == eventId && member.UserId == userId
                      && member.Status == EventStaffStatus.Active && member.Role == EventStaffRole.RaceStaff
                  select new EventStaffAssignment(member.Id, eventId, race.Name, member.StationId))
            .ToListAsync(cancellationToken);
}
