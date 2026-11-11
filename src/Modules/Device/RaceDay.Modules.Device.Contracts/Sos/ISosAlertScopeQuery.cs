namespace RaceDay.Modules.Device.Contracts.Sos;

/// <summary>
/// Cross-module read of a Device SOS id, event, and status.
/// Callers must not acknowledge or resolve the source SOS through this query.
/// </summary>
public interface ISosAlertScopeQuery
{
    Task<SosAlertScope?> GetAsync(Guid sosAlertId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SosAlertScope>> ListByEventAsync(Guid eventId, CancellationToken cancellationToken = default);
}

public sealed record SosAlertScope(Guid SosAlertId, Guid? EventId, string Status);
