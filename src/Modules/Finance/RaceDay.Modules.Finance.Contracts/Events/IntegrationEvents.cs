using RaceDay.Shared.Application.Abstractions;

namespace RaceDay.Modules.Finance.Contracts.Events;

public sealed record RefundCompletedIntegrationEvent(
    Guid RefundRequestId,
    Guid PaymentId,
    Guid RegistrationId,
    Guid RaceEventId,
    Guid UserId,
    decimal RefundAmount) : IIntegrationEvent
{
    public Guid EventId { get; } = Guid.NewGuid();
    public DateTime OccurredOnUtc { get; } = DateTime.UtcNow;
}
