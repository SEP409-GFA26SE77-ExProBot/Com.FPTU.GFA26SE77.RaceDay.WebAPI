namespace RaceDay.Modules.Event.Contracts.Events;

/// <summary>Cross-module: verify Active staff membership for an event (Photos RBAC).</summary>
public interface IEventStaffAccessQuery
{
    Task<bool> IsActivePhotographerAsync(
        Guid eventId,
        Guid userId,
        CancellationToken cancellationToken = default);
}
