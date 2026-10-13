namespace RaceDay.Modules.Event.Application.Packages;

/// <summary>Wire/DTO shapes for package catalog + quotes. Options themselves live in DB (Admin CRUD).</summary>
public static class EventPackageCatalog
{
    public sealed record DurationOption(string Name, int Months, decimal Price);
    public sealed record CapacityOption(string Name, int MaxParticipants, decimal AddOnFee);
    public sealed record QuoteLineItem(string Key, string Label, decimal Amount);
    public sealed record PackageQuote(
        IReadOnlyList<QuoteLineItem> LineItems,
        decimal Total,
        string Currency,
        string? AppliedPromoCode);

    public sealed record PromoCodeInfo(string Code, decimal PercentOff, DateTime? ExpiresAtUtc);

    public sealed record CatalogResponse(
        IReadOnlyList<DurationOption> Durations,
        IReadOnlyList<CapacityOption> Capacities,
        IReadOnlyList<PromoCodeInfo> PromoCodes);
}
