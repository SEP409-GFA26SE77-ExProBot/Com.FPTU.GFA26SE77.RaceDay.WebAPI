using RaceDay.Shared.Domain.Abstractions;

namespace RaceDay.Modules.Event.Domain.Events;

public sealed record RaceEventCreatedDomainEvent(Guid RaceEventId, string Name, Guid OrganizerUserId) : DomainEvent;

public sealed record RaceEventSubmittedDomainEvent(Guid RaceEventId, string Name, Guid OrganizerUserId) : DomainEvent;

public sealed record RaceEventApprovedDomainEvent(Guid RaceEventId, string Name, Guid OrganizerUserId) : DomainEvent;

public sealed record RaceEventRejectedDomainEvent(Guid RaceEventId, string Name, Guid OrganizerUserId, string Reason) : DomainEvent;
