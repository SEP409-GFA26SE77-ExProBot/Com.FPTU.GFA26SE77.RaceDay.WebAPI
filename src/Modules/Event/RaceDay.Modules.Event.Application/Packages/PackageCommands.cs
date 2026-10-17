using FluentValidation;
using MediatR;
using RaceDay.Modules.Event.Application.Abstractions;
using RaceDay.Modules.Event.Domain.Packages;
using RaceDay.Shared.Application.Abstractions;
using RaceDay.Shared.Domain.Exceptions;

namespace RaceDay.Modules.Event.Application.Packages;

// ── DTOs ──────────────────────────────────────────────────────────

public sealed record PackagePurchaseDto(
    Guid Id,
    Guid OrganizerUserId,
    string OrgName,
    string? OrgIntro,
    string? OrgAddress,
    string? InvoiceInfo,
    int DurationMonths,
    int ParticipantCap,
    string? PromoCode,
    decimal BasePrice,
    decimal CapacityFee,
    decimal Discount,
    decimal TotalAmount,
    string Currency,
    string Status,
    Guid? PaymentId,
    Guid? CreatedEventId,
    DateTime? PackageExpiresAtUtc,
    DateTime CreatedAtUtc);

public static class PackagePurchaseDtoMapper
{
    public static string ToApiStatus(PackagePurchaseStatus s) => s switch
    {
        PackagePurchaseStatus.PendingPayment => "pending_payment",
        PackagePurchaseStatus.Paid => "paid",
        PackagePurchaseStatus.Cancelled => "cancelled",
        PackagePurchaseStatus.Expired => "expired",
        _ => s.ToString().ToLowerInvariant()
    };

    public static PackagePurchaseDto ToDto(EventPackagePurchase p) => new(
        p.Id, p.OrganizerUserId, p.OrgName, p.OrgIntro, p.OrgAddress, p.InvoiceInfo,
        p.DurationMonths, p.ParticipantCap, p.PromoCode,
        p.BasePrice, p.CapacityFee, p.Discount, p.TotalAmount, p.Currency,
        ToApiStatus(p.Status), p.PaymentId, p.CreatedEventId,
        p.PackageExpiresAtUtc, p.CreatedAtUtc);
}

// ── Create purchase ───────────────────────────────────────────────

public sealed record CreatePackagePurchaseRequest(
    string OrgName,
    string? OrgIntro,
    string? OrgAddress,
    string? InvoiceInfo,
    int DurationMonths,
    int ParticipantCap,
    string? PromoCode);

public sealed record CreatePackagePurchaseCommand(
    string OrgName,
    string? OrgIntro,
    string? OrgAddress,
    string? InvoiceInfo,
    int DurationMonths,
    int ParticipantCap,
    string? PromoCode) : IRequest<PackagePurchaseDto>;

public sealed class CreatePackagePurchaseCommandValidator : AbstractValidator<CreatePackagePurchaseCommand>
{
    public CreatePackagePurchaseCommandValidator()
    {
        RuleFor(x => x.OrgName).NotEmpty().MaximumLength(200);
        RuleFor(x => x.OrgIntro).NotEmpty().MaximumLength(2000);
        RuleFor(x => x.OrgAddress).NotEmpty().MaximumLength(500);
        RuleFor(x => x.InvoiceInfo).MaximumLength(1000);
        RuleFor(x => x.DurationMonths).GreaterThan(0);
        RuleFor(x => x.ParticipantCap).GreaterThan(0);
        RuleFor(x => x.PromoCode).MaximumLength(50);
    }
}

public sealed class CreatePackagePurchaseCommandHandler(
    IEventPackagePurchaseRepository purchases,
    PackagePricingService pricing,
    ICurrentUserAccessor currentUser,
    IEventUnitOfWork unitOfWork) : IRequestHandler<CreatePackagePurchaseCommand, PackagePurchaseDto>
{
    public async Task<PackagePurchaseDto> Handle(CreatePackagePurchaseCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId
            ?? throw new DomainException("Cần đăng nhập.");

        var quote = await pricing.QuoteAsync(request.DurationMonths, request.ParticipantCap, request.PromoCode, cancellationToken);

        var purchase = EventPackagePurchase.Create(
            userId,
            request.OrgName,
            request.OrgIntro,
            request.OrgAddress,
            request.InvoiceInfo,
            request.DurationMonths,
            request.ParticipantCap,
            request.PromoCode?.Trim(),
            quote.LineItems.FirstOrDefault(l => l.Key == "base_duration")?.Amount ?? 0,
            quote.LineItems.FirstOrDefault(l => l.Key == "capacity_addon")?.Amount ?? 0,
            Math.Abs(quote.LineItems.FirstOrDefault(l => l.Key == "promo_discount")?.Amount ?? 0),
            quote.Total,
            quote.Currency);

        await purchases.AddAsync(purchase, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return PackagePurchaseDtoMapper.ToDto(purchase);
    }
}

// ── Get purchase ──────────────────────────────────────────────────

public sealed record GetPackagePurchaseQuery(Guid PurchaseId) : IRequest<PackagePurchaseDto>;

public sealed class GetPackagePurchaseQueryHandler(
    IEventPackagePurchaseRepository purchases,
    ICurrentUserAccessor currentUser) : IRequestHandler<GetPackagePurchaseQuery, PackagePurchaseDto>
{
    public async Task<PackagePurchaseDto> Handle(GetPackagePurchaseQuery request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new DomainException("Cần đăng nhập.");
        var purchase = await purchases.GetByIdAsync(request.PurchaseId, cancellationToken)
            ?? throw new DomainException("Không tìm thấy gói mua.");

        if (purchase.OrganizerUserId != userId && !currentUser.IsAdmin)
        {
            throw new DomainException("Không có quyền xem gói này.");
        }

        return PackagePurchaseDtoMapper.ToDto(purchase);
    }
}

// ── Cancel purchase (pending only) ────────────────────────────────

public sealed record CancelPackagePurchaseCommand(Guid PurchaseId) : IRequest<PackagePurchaseDto>;

public sealed class CancelPackagePurchaseCommandHandler(
    IEventPackagePurchaseRepository purchases,
    ICurrentUserAccessor currentUser,
    IEventUnitOfWork unitOfWork) : IRequestHandler<CancelPackagePurchaseCommand, PackagePurchaseDto>
{
    public async Task<PackagePurchaseDto> Handle(CancelPackagePurchaseCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new DomainException("Cần đăng nhập.");
        var purchase = await purchases.GetByIdAsync(request.PurchaseId, cancellationToken)
            ?? throw new DomainException("Không tìm thấy gói mua.");

        if (purchase.OrganizerUserId != userId && !currentUser.IsAdmin)
        {
            throw new DomainException("Không có quyền hủy gói này.");
        }

        if (purchase.Status == PackagePurchaseStatus.Cancelled)
        {
            return PackagePurchaseDtoMapper.ToDto(purchase);
        }

        purchase.MarkCancelled();
        purchases.Update(purchase);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return PackagePurchaseDtoMapper.ToDto(purchase);
    }
}

// ── Get catalog (async — promo from DB) ───────────────────────────

public sealed record GetPackageCatalogQuery : IRequest<EventPackageCatalog.CatalogResponse>;

public sealed class GetPackageCatalogQueryHandler(
    IPackageDurationOptionRepository durationRepo,
    IPackageCapacityOptionRepository capacityRepo,
    IPromoCodeRepository promoRepo) : IRequestHandler<GetPackageCatalogQuery, EventPackageCatalog.CatalogResponse>
{
    public async Task<EventPackageCatalog.CatalogResponse> Handle(GetPackageCatalogQuery request, CancellationToken cancellationToken)
    {
        var durations = await durationRepo.ListActiveAsync(cancellationToken);
        var capacities = await capacityRepo.ListActiveAsync(cancellationToken);
        var activePromos = await promoRepo.ListActiveAsync(DateTime.UtcNow, cancellationToken);

        return new EventPackageCatalog.CatalogResponse(
            durations.Select(d => new EventPackageCatalog.DurationOption(d.Name, d.Months, d.Price)).ToList(),
            capacities.Select(c => new EventPackageCatalog.CapacityOption(c.Name, c.MaxParticipants, c.AddOnFee)).ToList(),
            activePromos.Select(p => new EventPackageCatalog.PromoCodeInfo(p.Code, p.PercentOff, p.ExpiresAtUtc)).ToList());
    }
}

// ── Admin promo-code DTOs ─────────────────────────────────────────

public sealed record PromoCodeDto(
    Guid Id,
    string Code,
    decimal PercentOff,
    string Status,
    DateTime? ExpiresAtUtc,
    int? MaxRedemptions,
    int UsedCount,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc);

public static class PromoCodeDtoMapper
{
    public static PromoCodeDto ToDto(Domain.Packages.PromoCode p) => new(
        p.Id, p.Code, p.PercentOff,
        p.Status == Domain.Packages.PromoCodeStatus.Active ? "active" : "inactive",
        p.ExpiresAtUtc, p.MaxRedemptions, p.UsedCount,
        p.CreatedAtUtc, p.UpdatedAtUtc);
}

// ── Admin: list all promo codes ───────────────────────────────────

public sealed record ListPromoCodesQuery : IRequest<IReadOnlyList<PromoCodeDto>>;

public sealed class ListPromoCodesQueryHandler(
    IPromoCodeRepository promoRepo) : IRequestHandler<ListPromoCodesQuery, IReadOnlyList<PromoCodeDto>>
{
    public async Task<IReadOnlyList<PromoCodeDto>> Handle(ListPromoCodesQuery request, CancellationToken cancellationToken)
    {
        var all = await promoRepo.ListAllAsync(cancellationToken);
        return all.Select(PromoCodeDtoMapper.ToDto).ToList();
    }
}

// ── Admin: create promo code ──────────────────────────────────────

public sealed record CreatePromoCodeCommand(
    string Code,
    decimal PercentOff,
    DateTime? ExpiresAtUtc,
    int? MaxRedemptions) : IRequest<PromoCodeDto>;

public sealed class CreatePromoCodeCommandValidator : AbstractValidator<CreatePromoCodeCommand>
{
    public CreatePromoCodeCommandValidator()
    {
        RuleFor(x => x.Code).NotEmpty().MaximumLength(50);
        RuleFor(x => x.PercentOff).GreaterThan(0).LessThanOrEqualTo(1);
    }
}

public sealed class CreatePromoCodeCommandHandler(
    IPromoCodeRepository promoRepo,
    IEventUnitOfWork unitOfWork) : IRequestHandler<CreatePromoCodeCommand, PromoCodeDto>
{
    public async Task<PromoCodeDto> Handle(CreatePromoCodeCommand request, CancellationToken cancellationToken)
    {
        var code = request.Code.Trim().ToUpperInvariant();
        if (await promoRepo.ExistsByCodeAsync(code, cancellationToken))
        {
            throw new DomainException($"Mã '{code}' đã tồn tại.");
        }

        var promo = Domain.Packages.PromoCode.Create(
            code, request.PercentOff, request.ExpiresAtUtc, request.MaxRedemptions);

        await promoRepo.AddAsync(promo, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return PromoCodeDtoMapper.ToDto(promo);
    }
}

// ── Admin: update promo code (activate/deactivate/expiry/max) ─────

public sealed record UpdatePromoCodeCommand(
    Guid Id,
    bool? Active,
    DateTime? ExpiresAtUtc,
    bool ClearExpiry,
    int? MaxRedemptions,
    bool ClearMaxRedemptions) : IRequest<PromoCodeDto>;

public sealed class UpdatePromoCodeCommandHandler(
    IPromoCodeRepository promoRepo,
    IEventUnitOfWork unitOfWork) : IRequestHandler<UpdatePromoCodeCommand, PromoCodeDto>
{
    public async Task<PromoCodeDto> Handle(UpdatePromoCodeCommand request, CancellationToken cancellationToken)
    {
        var promo = await promoRepo.GetByIdAsync(request.Id, cancellationToken)
            ?? throw new DomainException("Promo code not found.");

        if (request.Active.HasValue)
        {
            if (request.Active.Value) promo.Activate();
            else promo.Deactivate();
        }

        if (request.ClearExpiry)
            promo.UpdateExpiry(null);
        else if (request.ExpiresAtUtc.HasValue)
            promo.UpdateExpiry(request.ExpiresAtUtc.Value);

        if (request.ClearMaxRedemptions)
            promo.UpdateMaxRedemptions(null);
        else if (request.MaxRedemptions.HasValue)
            promo.UpdateMaxRedemptions(request.MaxRedemptions.Value);

        promoRepo.Update(promo);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return PromoCodeDtoMapper.ToDto(promo);
    }
}
