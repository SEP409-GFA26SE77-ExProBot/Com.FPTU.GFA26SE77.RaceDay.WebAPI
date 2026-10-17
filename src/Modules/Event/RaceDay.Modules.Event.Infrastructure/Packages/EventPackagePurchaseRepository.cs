using Microsoft.EntityFrameworkCore;
using RaceDay.Modules.Event.Application.Abstractions;
using RaceDay.Modules.Event.Domain.Packages;
using RaceDay.Modules.Event.Infrastructure.Persistence;

namespace RaceDay.Modules.Event.Infrastructure.Packages;

public sealed class EventPackagePurchaseRepository(EventDbContext db) : IEventPackagePurchaseRepository
{
    public Task<EventPackagePurchase?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        => db.PackagePurchases.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

    public async Task<IReadOnlyList<EventPackagePurchase>> ListByOrganizerAsync(Guid organizerUserId, CancellationToken cancellationToken = default)
        => await db.PackagePurchases
            .Where(x => x.OrganizerUserId == organizerUserId)
            .OrderByDescending(x => x.CreatedAtUtc)
            .ToListAsync(cancellationToken);

    public async Task AddAsync(EventPackagePurchase purchase, CancellationToken cancellationToken = default)
        => await db.PackagePurchases.AddAsync(purchase, cancellationToken);

    public void Update(EventPackagePurchase purchase)
        => db.PackagePurchases.Update(purchase);
}
