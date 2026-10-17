using RaceDay.Shared.Application.Abstractions;

namespace RaceDay.Modules.Event.Contracts.Registrations;

/// <summary>Cross-module read model for Payment checkout (Event.Infrastructure implements).</summary>
public interface IRegistrationCheckoutInfoQuery
{
    Task<RegistrationCheckoutInfo?> GetAsync(Guid registrationId, CancellationToken cancellationToken = default);
}

public sealed record RegistrationCheckoutInfo(
    Guid RegistrationId,
    Guid UserId,
    Guid EventId,
    Guid DistanceId,
    string EventName,
    string DistanceType,
    string DistanceName,
    double DistanceKm,
    decimal Amount,
    string Currency,
    string RegistrationStatus);
