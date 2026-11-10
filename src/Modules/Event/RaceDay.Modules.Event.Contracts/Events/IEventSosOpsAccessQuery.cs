namespace RaceDay.Modules.Event.Contracts.Events;

/// <summary>
/// Cross-module: Device SOS dashboard — organizer của giải hoặc Race-staff Active trên giải.
/// </summary>
public interface IEventSosOpsAccessQuery
{
    /// <summary>True nếu user là chủ giải (OrganizerUserId) hoặc staff Active role Race-staff.</summary>
    Task<bool> CanManageSosAsync(
        Guid eventId,
        Guid userId,
        CancellationToken cancellationToken = default);
}
