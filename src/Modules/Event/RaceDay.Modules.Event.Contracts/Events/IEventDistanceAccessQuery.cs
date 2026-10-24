namespace RaceDay.Modules.Event.Contracts.Events;

/// <summary>Cross-module read: Map verifies event/distance ownership & visibility.</summary>
public interface IEventDistanceAccessQuery
{
    Task<EventDistanceAccessInfo?> GetAsync(
        Guid eventId,
        Guid distanceId,
        CancellationToken cancellationToken = default);
}

public sealed record EventDistanceAccessInfo(
    Guid EventId,
    Guid DistanceId,
    Guid OrganizerUserId,
    string DistanceName,
    double DistanceKm,
    /// <summary>RaceEventStatus name (Draft, Approved, RegistrationOpen, …).</summary>
    string Status);
