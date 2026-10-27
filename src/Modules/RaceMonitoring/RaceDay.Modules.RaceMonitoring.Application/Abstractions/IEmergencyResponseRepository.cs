using RaceDay.Modules.RaceMonitoring.Domain;
using RaceDay.Modules.RaceMonitoring.Domain.EmergencyCoordination;

namespace RaceDay.Modules.RaceMonitoring.Application.Abstractions;

public interface IEmergencyResponseRepository
{
    Task<EmergencyResponse?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<(IReadOnlyList<EmergencyResponse> Items, int Total)> ListBySourceSosIdsAsync(
        IReadOnlyCollection<Guid> sourceSosAlertIds,
        EmergencyResponseStatus? status,
        EmergencyPriority? priority,
        EmergencyIncidentType? incidentType,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);
}
