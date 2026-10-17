namespace RaceDay.Modules.Event.Contracts.Events;

/// <summary>
/// Cross-module: Device SOS dashboard — organizer của giải hoặc Coordinator Active trên giải.
/// </summary>
public interface IEventSosOpsAccessQuery
{
    /// <summary>True nếu user là chủ giải (OrganizerUserId) hoặc staff Active role Coordinator.</summary>
    Task<bool> CanManageSosAsync(
        Guid eventId,
        Guid userId,
        CancellationToken cancellationToken = default);
}
