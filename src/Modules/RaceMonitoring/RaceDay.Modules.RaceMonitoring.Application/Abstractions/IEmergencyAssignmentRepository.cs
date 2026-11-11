using RaceDay.Modules.RaceMonitoring.Domain.EmergencyCoordination;

namespace RaceDay.Modules.RaceMonitoring.Application.Abstractions;

public interface IEmergencyAssignmentRepository
{
    Task<EmergencyAssignment?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<EmergencyAssignment?> GetWithResourceAsync(Guid id, CancellationToken cancellationToken = default);

    Task<bool> ExistsForResponseAsync(
        Guid emergencyResponseId,
        Guid emergencyResourceId,
        CancellationToken cancellationToken = default);

    Task<(IReadOnlyList<EmergencyAssignment> Items, int Total)> ListByResponseAsync(
        Guid emergencyResponseId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<EmergencyAssignment>> ListTrackedByResponseAsync(
        Guid emergencyResponseId,
        CancellationToken cancellationToken = default);

    void Add(EmergencyAssignment assignment);

    void Remove(EmergencyAssignment assignment);
}
