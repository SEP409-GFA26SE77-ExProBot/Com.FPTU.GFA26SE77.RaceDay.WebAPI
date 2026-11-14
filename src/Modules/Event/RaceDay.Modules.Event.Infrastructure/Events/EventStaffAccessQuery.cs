using Microsoft.EntityFrameworkCore;
using RaceDay.Modules.Event.Contracts.Events;
using RaceDay.Modules.Event.Domain.Staff;
using RaceDay.Modules.Event.Infrastructure.Persistence;

namespace RaceDay.Modules.Event.Infrastructure.Events;

public sealed class EventStaffAccessQuery(EventDbContext db) : IEventStaffAccessQuery, IEventPositioningAccessQuery
{
    public async Task<IReadOnlyList<PositioningStaffAssignment>> ListActiveAsync(
        Guid eventId, Guid userId, CancellationToken cancellationToken = default)
    {
        var members = await db.StaffMembers.AsNoTracking()
            .Where(x => x.EventId == eventId && x.UserId == userId && x.Status == EventStaffStatus.Active)
            .ToListAsync(cancellationToken);

        return members.Select(member => new PositioningStaffAssignment(
            member.Id,
            member.FullName,
            member.Role == EventStaffRole.Photographer ? "photographer" : "race-staff",
            member.GetStations().Select(station => station.StationId).ToArray())).ToArray();
    }

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
