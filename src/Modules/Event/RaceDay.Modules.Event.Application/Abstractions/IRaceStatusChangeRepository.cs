using RaceDay.Modules.Event.Domain.Registrations;

namespace RaceDay.Modules.Event.Application.Abstractions;

public interface IRaceStatusChangeRepository
{
    Task<RaceStatusChange?> GetLatestAsync(Guid registrationId, CancellationToken cancellationToken = default);
    Task AddAsync(RaceStatusChange change, CancellationToken cancellationToken = default);
}
