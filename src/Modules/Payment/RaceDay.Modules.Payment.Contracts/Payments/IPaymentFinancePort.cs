namespace RaceDay.Modules.Payment.Contracts.Payments;

public interface IPaymentFinancePort
{
    Task<PaymentFinanceInfo?> GetByIdAsync(Guid paymentId, CancellationToken cancellationToken = default);
    Task<PaymentFinanceInfo?> GetPaidByRegistrationAsync(Guid registrationId, CancellationToken cancellationToken = default);
    Task MarkRefundedAsync(Guid paymentId, CancellationToken cancellationToken = default);
}

public sealed record PaymentFinanceInfo(
    Guid PaymentId,
    Guid RegistrationId,
    Guid UserId,
    decimal Amount,
    string Currency,
    string Status,
    DateTime? PaidAtUtc);
