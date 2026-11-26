using RaceDay.Modules.RaceMonitoring.Domain;

namespace RaceDay.Modules.RaceMonitoring.Application.Abstractions;

public interface IRfidTagAssignmentRepository
{
    Task<RfidTagAssignment?> FindByTagAsync(
        Guid eventId,
        string tagScheme,
        string tagId,
        CancellationToken cancellationToken = default);

    Task<RfidTagAssignment?> FindByRegistrationAsync(
        Guid eventId,
        Guid registrationId,
        CancellationToken cancellationToken = default);

    void Add(RfidTagAssignment assignment);
}
