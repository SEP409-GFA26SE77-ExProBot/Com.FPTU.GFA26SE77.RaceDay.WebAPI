namespace RaceDay.Modules.Event.Contracts.Packages;

/// <summary>Cross-module read model for Payment package checkout (Event.Infrastructure implements).</summary>
public interface IPackageCheckoutInfoQuery
{
    Task<PackageCheckoutInfo?> GetAsync(Guid purchaseId, CancellationToken cancellationToken = default);
}

public sealed record PackageCheckoutInfo(
    Guid PurchaseId,
    Guid UserId,
    decimal ListAmount,
    decimal DiscountAmount,
    string? PromoCode,
    decimal TotalAmount,
    string Currency,
    string Status,
    string Description);
