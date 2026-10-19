using RaceDay.Shared.Application.Abstractions;

namespace RaceDay.Modules.Device.Contracts.Events;

public sealed record WearDeviceLinkedIntegrationEvent(
    Guid LinkedDeviceId,
    Guid RunnerUserId,
    string ClientDeviceId) : IIntegrationEvent
{
    public Guid EventId { get; } = Guid.NewGuid();
    public DateTime OccurredOnUtc { get; } = DateTime.UtcNow;
}

public sealed record SosOpenedIntegrationEvent(
    Guid SosAlertId,
    Guid RunnerUserId,
    Guid? RaceEventId,
    double Lat,
    double Lng) : IIntegrationEvent
{
    public Guid EventId { get; } = Guid.NewGuid();
    public DateTime OccurredOnUtc { get; } = DateTime.UtcNow;
}
