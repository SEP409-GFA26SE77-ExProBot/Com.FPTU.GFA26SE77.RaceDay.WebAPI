using RaceDay.Shared.Application.Abstractions;

namespace RaceDay.Modules.Payment.Contracts.Events;

public sealed record PaymentCompletedIntegrationEvent(
    Guid PaymentId,
    Guid RegistrationId,
    Guid UserId,
    decimal Amount,
    string Currency,
    string Gateway,
    string? TransactionReference) : IIntegrationEvent
{
    public Guid EventId { get; } = Guid.NewGuid();
    public DateTime OccurredOnUtc { get; } = DateTime.UtcNow;
}

public sealed record PackagePaymentCompletedIntegrationEvent(
    Guid PaymentId,
    Guid PackagePurchaseId,
    Guid UserId,
    decimal Amount,
    string Currency,
    string Gateway,
    string? TransactionReference) : IIntegrationEvent
{
    public Guid EventId { get; } = Guid.NewGuid();
    public DateTime OccurredOnUtc { get; } = DateTime.UtcNow;
}

public sealed record PaymentFailedIntegrationEvent(
    Guid PaymentId,
    Guid RegistrationId,
    string Reason) : IIntegrationEvent
{
    public Guid EventId { get; } = Guid.NewGuid();
    public DateTime OccurredOnUtc { get; } = DateTime.UtcNow;
}
