using Microsoft.EntityFrameworkCore;
using RaceDay.Modules.Event.Contracts.Packages;
using RaceDay.Modules.Event.Domain.Packages;
using RaceDay.Modules.Event.Infrastructure.Persistence;

namespace RaceDay.Modules.Event.Infrastructure.Packages;

public sealed class PackageCheckoutInfoQuery(EventDbContext db) : IPackageCheckoutInfoQuery
{
    public async Task<PackageCheckoutInfo?> GetAsync(Guid purchaseId, CancellationToken cancellationToken = default)
    {
        var p = await db.PackagePurchases.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == purchaseId, cancellationToken);
        if (p is null)
        {
            return null;
        }

        var status = p.Status switch
        {
            PackagePurchaseStatus.PendingPayment => "pending_payment",
            PackagePurchaseStatus.Paid => "paid",
            PackagePurchaseStatus.Cancelled => "cancelled",
            PackagePurchaseStatus.Expired => "expired",
            _ => p.Status.ToString().ToLowerInvariant()
        };

        var listAmount = p.BasePrice + p.CapacityFee;

        return new PackageCheckoutInfo(
            p.Id,
            p.OrganizerUserId,
            listAmount,
            p.Discount,
            p.PromoCode,
            p.TotalAmount,
            p.Currency,
            status,
            $"Gói sự kiện {p.DurationMonths}T — {p.ParticipantCap} VĐV");
    }
}
