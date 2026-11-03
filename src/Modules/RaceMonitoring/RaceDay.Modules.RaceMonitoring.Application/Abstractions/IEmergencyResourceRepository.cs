using RaceDay.Modules.RaceMonitoring.Domain;
using RaceDay.Modules.RaceMonitoring.Domain.EmergencyCoordination;

namespace RaceDay.Modules.RaceMonitoring.Application.Abstractions;

public interface IEmergencyResourceRepository
{
    Task<EmergencyResource?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<(IReadOnlyList<EmergencyResource> Items, int Total)> ListByEventAsync(
        Guid eventId,
        EmergencyResourceStatus? status,
        EmergencyResourceType? resourceType,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);
}
