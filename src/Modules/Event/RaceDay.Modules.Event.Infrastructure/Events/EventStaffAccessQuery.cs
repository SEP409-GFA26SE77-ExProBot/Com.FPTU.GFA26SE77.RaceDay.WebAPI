using Microsoft.EntityFrameworkCore;
using RaceDay.Modules.Event.Contracts.Events;
using RaceDay.Modules.Event.Domain.Staff;
using RaceDay.Modules.Event.Infrastructure.Persistence;

namespace RaceDay.Modules.Event.Infrastructure.Events;

public sealed class EventStaffAccessQuery(EventDbContext db) : IEventStaffAccessQuery
{
    public Task<bool> IsActivePhotographerAsync(
        Guid eventId,
        Guid userId,
        CancellationToken cancellationToken = default)
        => db.StaffMembers.AsNoTracking().AnyAsync(
            x => x.EventId == eventId
                 && x.UserId == userId
                 && x.Role == EventStaffRole.Photographer
                 && x.Status == EventStaffStatus.Active,
            cancellationToken);

    public Task<bool> ExistsActiveStaffUserAsync(
        Guid eventId,
        Guid userId,
        CancellationToken cancellationToken = default)
        => db.StaffMembers.AsNoTracking().AnyAsync(
            x => x.EventId == eventId
                 && x.UserId == userId
                 && x.Status == EventStaffStatus.Active,
            cancellationToken);
}
