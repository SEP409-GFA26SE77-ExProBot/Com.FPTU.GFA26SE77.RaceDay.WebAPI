namespace RaceDay.Modules.Event.Contracts.Finance;

/// <summary>Includes cancelled/refunded registrations to preserve historical cash flow.</summary>
public interface IOrganizerRegistrationScopeQuery
{
    Task<IReadOnlyList<Guid>> ListIdsAsync(Guid organizerId, Guid? eventId, CancellationToken ct);
}
