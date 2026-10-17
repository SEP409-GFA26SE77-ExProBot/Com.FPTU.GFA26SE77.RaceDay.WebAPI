using RaceDay.Modules.Event.Application.Abstractions;
using RaceDay.Shared.Domain.Exceptions;

namespace RaceDay.Modules.Event.Application.Packages;

public sealed class PackagePricingService(
    IPackageDurationOptionRepository durations,
    IPackageCapacityOptionRepository capacities,
    IPromoCodeRepository promoRepo)
{
    public async Task<EventPackageCatalog.PackageQuote> QuoteAsync(
        int durationMonths,
        int capacity,
        string? promoCode = null,
        CancellationToken cancellationToken = default)
    {
        var dur = await durations.GetActiveByMonthsAsync(durationMonths, cancellationToken)
                  ?? throw new DomainException($"Thời hạn {durationMonths} tháng không hợp lệ hoặc đã ngừng bán.");
        var cap = await capacities.GetActiveByCapAsync(capacity, cancellationToken)
                  ?? throw new DomainException($"Sức chứa {capacity} không hợp lệ hoặc đã ngừng bán.");

        var basePrice = dur.Price;
        var capacityFee = cap.AddOnFee;
        var subtotal = basePrice + capacityFee;

        decimal discount = 0;
        string? appliedPromo = null;

        if (!string.IsNullOrWhiteSpace(promoCode))
        {
            var promo = await promoRepo.GetByCodeAsync(promoCode.Trim(), cancellationToken)
                ?? throw new DomainException($"Mã khuyến mãi '{promoCode}' không hợp lệ.");

            promo.EnsureRedeemable(DateTime.UtcNow);

            discount = Math.Round(subtotal * promo.PercentOff, 0, MidpointRounding.AwayFromZero);
            appliedPromo = promo.Code;
        }

        var total = subtotal - discount;

        return new EventPackageCatalog.PackageQuote(
        [
            new EventPackageCatalog.QuoteLineItem("base_duration", dur.Name, basePrice),
            new EventPackageCatalog.QuoteLineItem("capacity_addon", cap.Name, capacityFee),
            ..( discount > 0
                ? [new EventPackageCatalog.QuoteLineItem("promo_discount", $"Khuyến mãi {appliedPromo}", -discount)]
                : Array.Empty<EventPackageCatalog.QuoteLineItem>()),
        ], total, "VND", appliedPromo);
    }
}
