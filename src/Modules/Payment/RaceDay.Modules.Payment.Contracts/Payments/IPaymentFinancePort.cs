namespace RaceDay.Modules.Payment.Contracts.Payments;

public interface IPaymentFinancePort
{
    Task<PaymentFinanceInfo?> GetByIdAsync(Guid paymentId, CancellationToken cancellationToken = default);
    Task<PaymentFinanceInfo?> GetPaidByRegistrationAsync(Guid registrationId, CancellationToken cancellationToken = default);
    /// <summary>
    /// Payment đã thu tiền (Paid hoặc Refunded) — dùng reconcile GrossRevenue
    /// (gross giữ nguyên sau hoàn; RefundedTotal riêng).
    /// </summary>
    Task<PaymentFinanceInfo?> GetSettledByRegistrationAsync(
        Guid registrationId,
        CancellationToken cancellationToken = default);
    /// <summary>Gọi PayOS (mock/payout) rồi đánh dấu payment Refunded.</summary>
    Task MarkRefundedAsync(Guid paymentId, decimal refundAmount, CancellationToken cancellationToken = default);
}

public sealed record PaymentFinanceInfo(
    Guid PaymentId,
    Guid RegistrationId,
    Guid UserId,
    decimal Amount,
    string Currency,
    string Status,
    DateTime? PaidAtUtc);
