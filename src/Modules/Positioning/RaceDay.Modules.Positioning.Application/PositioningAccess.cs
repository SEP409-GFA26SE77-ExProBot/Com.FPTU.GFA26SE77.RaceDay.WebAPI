using RaceDay.Modules.Event.Contracts.Events;

namespace RaceDay.Modules.Positioning.Application;

public sealed class PositioningAccess(IEventOrganizerAccessQuery organizers, IEventPositioningAccessQuery staff)
{
    public async Task<bool> CanWatchAsync(Guid eventId, Guid userId, CancellationToken ct)
        => (await organizers.GetAsync(eventId, ct))?.OrganizerUserId == userId
            || (await staff.ListActiveAsync(eventId, userId, ct)).Count > 0;

    public async Task<PositioningReportAccess?> GetReportingAccessAsync(Guid eventId, Guid userId, CancellationToken ct)
    {
        var race = await organizers.GetAsync(eventId, ct);
        if (race is null) return null;

        var assignments = await staff.ListActiveAsync(eventId, userId, ct);
        if (assignments.Count > 0 || race.OrganizerUserId == userId)
        {
            var alertsStartAt = race.Status is "Cancelled" or "Completed" or "Suspended"
                ? null
                : race.StartsAtUtc?.AddHours(-1);
            return new PositioningReportAccess(assignments, alertsStartAt);
        }
        return null;
    }
}

public sealed record PositioningReportAccess(IReadOnlyList<PositioningStaffAssignment> Assignments, DateTimeOffset? AlertsStartAt);
