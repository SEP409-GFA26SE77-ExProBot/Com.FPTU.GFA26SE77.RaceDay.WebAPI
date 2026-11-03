using Microsoft.EntityFrameworkCore;
using RaceDay.Modules.Event.Application.Abstractions;
using RaceDay.Modules.Event.Domain.Registrations;
using RaceDay.Modules.Event.Infrastructure.Persistence;

namespace RaceDay.Modules.Event.Infrastructure.Repositories;

public sealed class RaceStatusChangeRepository(EventDbContext db) : IRaceStatusChangeRepository
{
    public Task<RaceStatusChange?> GetLatestAsync(Guid registrationId, CancellationToken cancellationToken = default)
        => db.RaceStatusChanges.AsNoTracking().Where(x => x.RegistrationId == registrationId)
            .OrderByDescending(x => x.ChangedAtUtc).ThenByDescending(x => x.Id)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task AddAsync(RaceStatusChange change, CancellationToken cancellationToken = default)
        => await db.RaceStatusChanges.AddAsync(change, cancellationToken);
}
