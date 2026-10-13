using MediatR;
using Microsoft.Extensions.Logging;
using RaceDay.Modules.Event.Application.Abstractions;
using RaceDay.Modules.Event.Domain.Events;
using RaceDay.Modules.Payment.Contracts.Events;
using RaceDay.Shared.Application.Messaging;
using RaceDay.Shared.Domain.Exceptions;

namespace RaceDay.Modules.Event.Application.Packages;

public sealed class PackagePaymentCompletedEventHandler(
    IEventPackagePurchaseRepository purchases,
    IPromoCodeRepository promoCodes,
    IRaceEventRepository events,
    IEventUnitOfWork unitOfWork,
    ILogger<PackagePaymentCompletedEventHandler> logger) : INotificationHandler<IntegrationEventOccurred>
{
    public async Task Handle(IntegrationEventOccurred notification, CancellationToken cancellationToken)
    {
        if (notification.Event is not PackagePaymentCompletedIntegrationEvent paid)
        {
            return;
        }

        var purchase = await purchases.GetByIdAsync(paid.PackagePurchaseId, cancellationToken)
            ?? throw new DomainException($"PackagePurchase {paid.PackagePurchaseId} not found.");

        if (purchase.Status == Domain.Packages.PackagePurchaseStatus.Paid)
        {
            return;
        }

        purchase.MarkPaid(paid.PaymentId);

        if (!string.IsNullOrWhiteSpace(purchase.PromoCode))
        {
            var promo = await promoCodes.GetByCodeAsync(purchase.PromoCode, cancellationToken);
            promo?.RecordRedemption();
            if (promo is not null) promoCodes.Update(promo);
        }

        var expiresAtUtc = DateTime.UtcNow
            .AddMonths(purchase.DurationMonths)
            .AddDays(7);

        // Liên hệ thật do organizer cập nhật ở Hồ sơ giải — không bịa email/SĐT.
        var organizer = OrganizerInfo.Create(
            purchase.OrgName,
            $"pending-{paid.UserId:N}@update.raceday.vn",
            "0000000000");

        var location = string.IsNullOrWhiteSpace(purchase.OrgAddress)
            ? "Chưa cập nhật địa điểm"
            : purchase.OrgAddress!.Trim();

        var race = RaceEvent.CreateDraft(
            paid.UserId,
            purchase.OrgName,
            location,
            DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(purchase.DurationMonths)),
            new GeoPoint(0, 0),
            organizer,
            purchase.ParticipantCap,
            description: string.IsNullOrWhiteSpace(purchase.OrgIntro) ? null : purchase.OrgIntro.Trim());

        race.SetPackageInfo(purchase.Id, expiresAtUtc);

        // Không tạo cự ly mặc định — BTC thêm ở Hồ sơ giải / Cự ly & giá vé trước khi gửi duyệt.

        purchase.SetCreatedEvent(race.Id, expiresAtUtc);

        await events.AddAsync(race, cancellationToken);
        purchases.Update(purchase);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Package purchase {PurchaseId} paid → draft event {EventId} created (no distances; expires {ExpiresAt})",
            purchase.Id, race.Id, expiresAtUtc);
    }
}
