using RaceDay.Shared.Application.Abstractions;

namespace RaceDay.Modules.Iam.Contracts.Events;

public sealed record UserCreatedIntegrationEvent(Guid UserId, string Email, string FullName) : IIntegrationEvent
{
    public Guid EventId { get; } = Guid.NewGuid();
    public DateTime OccurredOnUtc { get; } = DateTime.UtcNow;
}
