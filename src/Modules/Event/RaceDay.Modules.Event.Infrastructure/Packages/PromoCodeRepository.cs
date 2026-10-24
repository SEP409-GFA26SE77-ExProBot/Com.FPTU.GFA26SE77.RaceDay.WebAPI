using Microsoft.EntityFrameworkCore;
using RaceDay.Modules.Event.Application.Abstractions;
using RaceDay.Modules.Event.Domain.Packages;
using RaceDay.Modules.Event.Infrastructure.Persistence;

namespace RaceDay.Modules.Event.Infrastructure.Packages;

public sealed class PromoCodeRepository(EventDbContext db) : IPromoCodeRepository
{
    public Task<PromoCode?> GetByCodeAsync(string code, CancellationToken cancellationToken = default)
        => db.PromoCodes.FirstOrDefaultAsync(
            x => x.Code == code.Trim().ToUpperInvariant(), cancellationToken);

    public Task<PromoCode?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        => db.PromoCodes.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

    public async Task<IReadOnlyList<PromoCode>> ListAllAsync(CancellationToken cancellationToken = default)
        => await db.PromoCodes.OrderBy(x => x.Code).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<PromoCode>> ListActiveAsync(DateTime now, CancellationToken cancellationToken = default)
        => await db.PromoCodes
            .Where(x => x.Status == PromoCodeStatus.Active)
            .Where(x => !x.ExpiresAtUtc.HasValue || x.ExpiresAtUtc.Value > now)
            .OrderBy(x => x.Code)
            .ToListAsync(cancellationToken);

    public Task<bool> ExistsByCodeAsync(string code, CancellationToken cancellationToken = default)
        => db.PromoCodes.AnyAsync(
            x => x.Code == code.Trim().ToUpperInvariant(), cancellationToken);

    public async Task AddAsync(PromoCode promo, CancellationToken cancellationToken = default)
        => await db.PromoCodes.AddAsync(promo, cancellationToken);

    public void Update(PromoCode promo)
        => db.PromoCodes.Update(promo);
}
