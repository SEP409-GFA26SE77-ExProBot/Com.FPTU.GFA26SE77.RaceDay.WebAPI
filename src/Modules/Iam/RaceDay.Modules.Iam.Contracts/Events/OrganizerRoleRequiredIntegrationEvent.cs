using RaceDay.Shared.Application.Abstractions;

namespace RaceDay.Modules.Iam.Contracts.Events;

/// <summary>
/// Integration event published by Event module when a new race event is created.
/// Signals IAM module that the event organizer should be assigned the Organizer role.
/// </summary>
public sealed record OrganizerRoleRequiredIntegrationEvent(Guid UserId) : IIntegrationEvent
{
    public Guid EventId { get; } = Guid.NewGuid();
    public DateTime OccurredOnUtc { get; } = DateTime.UtcNow;
}
