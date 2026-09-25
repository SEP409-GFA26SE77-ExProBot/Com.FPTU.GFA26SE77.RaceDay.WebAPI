using RaceDay.Shared.Application.Abstractions;

namespace RaceDay.Modules.Iam.Contracts.Events;

public sealed record UserDeletedIntegrationEvent(Guid UserId, string Email) : IIntegrationEvent
{
    public Guid EventId { get; } = Guid.NewGuid();
    public DateTime OccurredOnUtc { get; } = DateTime.UtcNow;
}
