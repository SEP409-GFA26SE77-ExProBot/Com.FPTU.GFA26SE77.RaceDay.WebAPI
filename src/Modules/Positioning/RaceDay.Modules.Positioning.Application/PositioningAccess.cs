using RaceDay.Modules.Event.Contracts.Events;

namespace RaceDay.Modules.Positioning.Application;

public sealed class PositioningAccess(IEventOrganizerAccessQuery organizers, IEventPositioningAccessQuery staff)
{
    public async Task<bool> CanWatchAsync(Guid eventId, Guid userId, CancellationToken ct)
        => (await organizers.GetAsync(eventId, ct))?.OrganizerUserId == userId
            || (await staff.ListActiveAsync(eventId, userId, ct)).Count > 0;

    public async Task<IReadOnlyList<PositioningStaffAssignment>?> GetReportingAssignmentsAsync(Guid eventId, Guid userId, CancellationToken ct)
    {
        var assignments = await staff.ListActiveAsync(eventId, userId, ct);
        if (assignments.Count > 0 || (await organizers.GetAsync(eventId, ct))?.OrganizerUserId == userId)
        {
            return assignments;
        }
        return null;
    }
}
