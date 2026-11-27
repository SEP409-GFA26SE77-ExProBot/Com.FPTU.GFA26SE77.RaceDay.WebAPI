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
        var isOrganizer = race.OrganizerUserId == userId;
        if (assignments.Count > 0 || isOrganizer)
        {
            return new PositioningReportAccess(assignments, RaceDayOpensAt(race), isOrganizer);
        }
        return null;
    }

    public Task<EventOrganizerAccessInfo?> GetRaceAsync(Guid eventId, CancellationToken ct) => organizers.GetAsync(eventId, ct);

    /// <summary>
    /// Out-of-zone alerts and staff QR check-in open one hour before the race starts.
    /// Null when the race has no start time or is cancelled, completed or suspended.
    /// </summary>
    public static DateTimeOffset? RaceDayOpensAt(EventOrganizerAccessInfo race)
        => race.Status is "Cancelled" or "Completed" or "Suspended"
            ? null
            : race.StartsAtUtc?.AddHours(-1);
}

/// <param name="AlertsStartAt">When out-of-zone alerts start (see <see cref="PositioningAccess.RaceDayOpensAt"/>).</param>
/// <param name="IsOrganizer">Organizers report without QR check-in.</param>
public sealed record PositioningReportAccess(
    IReadOnlyList<PositioningStaffAssignment> Assignments, DateTimeOffset? AlertsStartAt, bool IsOrganizer = false);
