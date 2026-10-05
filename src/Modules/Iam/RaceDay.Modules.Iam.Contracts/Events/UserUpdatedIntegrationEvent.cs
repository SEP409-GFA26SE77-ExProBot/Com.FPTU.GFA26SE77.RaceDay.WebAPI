using RaceDay.Shared.Application.Abstractions;

namespace RaceDay.Modules.Iam.Contracts.Events;

public sealed record UserUpdatedIntegrationEvent(Guid UserId, string Email, string Username, string FullName) : IIntegrationEvent
{
    public Guid EventId { get; } = Guid.NewGuid();
    public DateTime OccurredOnUtc { get; } = DateTime.UtcNow;
}
