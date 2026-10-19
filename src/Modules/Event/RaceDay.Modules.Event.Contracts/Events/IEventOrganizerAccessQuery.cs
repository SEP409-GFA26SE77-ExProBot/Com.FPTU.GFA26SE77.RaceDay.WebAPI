namespace RaceDay.Modules.Event.Contracts.Events;

/// <summary>Cross-module read: Device/Map verify event ownership.</summary>
public interface IEventOrganizerAccessQuery
{
    Task<EventOrganizerAccessInfo?> GetAsync(Guid eventId, CancellationToken cancellationToken = default);
}

public sealed record EventOrganizerAccessInfo(
    Guid EventId,
    Guid OrganizerUserId,
    /// <summary>RaceEventStatus name (Draft, Approved, RegistrationOpen, …).</summary>
    string Status,
    string Name);
