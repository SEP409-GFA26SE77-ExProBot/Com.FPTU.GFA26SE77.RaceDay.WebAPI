namespace RaceDay.Modules.Event.Contracts.Events;

/// <summary>Cross-module: verify Active staff membership for an event (Photos / Map RBAC).</summary>
public interface IEventStaffAccessQuery
{
    Task<bool> IsActivePhotographerAsync(
        Guid eventId,
        Guid userId,
        CancellationToken cancellationToken = default);

    /// <summary>Any Active staff role (Coordinator / Photographer / Volunteer).</summary>
    Task<bool> ExistsActiveStaffUserAsync(
        Guid eventId,
        Guid userId,
        CancellationToken cancellationToken = default);
}
