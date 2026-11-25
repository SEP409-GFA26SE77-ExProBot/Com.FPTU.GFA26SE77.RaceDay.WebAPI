namespace RaceDay.Modules.Payment.Contracts.Payments;

public sealed record PaymentRevenueDay(DateOnly Date, string Currency, decimal Collected, decimal Refunded);

public interface IPaymentRevenueQuery
{
    Task<IReadOnlyList<PaymentRevenueDay>> ReadAsync(IReadOnlyList<Guid> registrationIds,
        DateTime fromUtc, DateTime toUtcExclusive, CancellationToken ct);
}
