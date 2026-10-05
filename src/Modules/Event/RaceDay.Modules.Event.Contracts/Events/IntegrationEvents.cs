using RaceDay.Shared.Application.Abstractions;

namespace RaceDay.Modules.Event.Contracts.Events;

public sealed record RaceEventCreatedIntegrationEvent(Guid RaceEventId, string Name, Guid OrganizerUserId) : IIntegrationEvent
{
    public Guid EventId { get; } = Guid.NewGuid();
    public DateTime OccurredOnUtc { get; } = DateTime.UtcNow;
}

public sealed record RaceEventSubmittedIntegrationEvent(Guid RaceEventId, string Name, Guid OrganizerUserId) : IIntegrationEvent
{
    public Guid EventId { get; } = Guid.NewGuid();
    public DateTime OccurredOnUtc { get; } = DateTime.UtcNow;
}

public sealed record RaceEventApprovedIntegrationEvent(Guid RaceEventId, string Name, Guid OrganizerUserId) : IIntegrationEvent
{
    public Guid EventId { get; } = Guid.NewGuid();
    public DateTime OccurredOnUtc { get; } = DateTime.UtcNow;
}

public sealed record RaceEventRejectedIntegrationEvent(Guid RaceEventId, string Name, Guid OrganizerUserId, string Reason) : IIntegrationEvent
{
    public Guid EventId { get; } = Guid.NewGuid();
    public DateTime OccurredOnUtc { get; } = DateTime.UtcNow;
}

public sealed record RegistrationCreatedIntegrationEvent(Guid RegistrationId, Guid RaceEventId, Guid UserId) : IIntegrationEvent
{
    public Guid EventId { get; } = Guid.NewGuid();
    public DateTime OccurredOnUtc { get; } = DateTime.UtcNow;
}

public sealed record RegistrationConfirmedIntegrationEvent(
    Guid RegistrationId,
    Guid RaceEventId,
    Guid UserId,
    string BibNumber,
    string TicketCode) : IIntegrationEvent
{
    public Guid EventId { get; } = Guid.NewGuid();
    public DateTime OccurredOnUtc { get; } = DateTime.UtcNow;
}

public sealed record RegistrationRejectedIntegrationEvent(
    Guid RegistrationId,
    Guid RaceEventId,
    Guid UserId,
    string Reason) : IIntegrationEvent
{
    public Guid EventId { get; } = Guid.NewGuid();
    public DateTime OccurredOnUtc { get; } = DateTime.UtcNow;
}
