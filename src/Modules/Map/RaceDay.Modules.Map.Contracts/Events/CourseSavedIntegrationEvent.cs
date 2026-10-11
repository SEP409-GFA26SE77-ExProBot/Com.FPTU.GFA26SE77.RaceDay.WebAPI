using RaceDay.Shared.Application.Abstractions;

namespace RaceDay.Modules.Map.Contracts.Events;

public sealed record CourseSavedIntegrationEvent(
    Guid RaceEventId,
    Guid DistanceId,
    string Title,
    double DistanceKm,
    Guid UpdatedByUserId) : IIntegrationEvent
{
    public Guid EventId { get; } = Guid.NewGuid();
    public DateTime OccurredOnUtc { get; } = DateTime.UtcNow;
}
