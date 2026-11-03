namespace RaceDay.Modules.RaceMonitoring.Application.Abstractions;

public interface IRaceEventLogPublisher
{
    void Publish(RaceEventLogNotification notification);
}

public interface IRaceEventLogBroker : IRaceEventLogPublisher
{
    IRaceEventLogSubscription Subscribe(Guid eventId);
}

public interface IRaceEventLogSubscription : IDisposable
{
    ValueTask<bool> WaitToReadAsync(CancellationToken cancellationToken);

    bool TryRead(out RaceEventLogNotification notification);

    bool IsOverrun { get; }
}

public sealed record RaceEventLogNotification(
    Guid Id,
    long Sequence,
    Guid EventId,
    Guid? EventDistanceId,
    string EventType,
    DateTime OccurredAtUtc,
    DateTime RecordedAtUtc,
    string? BibNumber,
    Guid? CheckpointId,
    Guid? RegistrationId,
    string? SourceType,
    Guid? SourceId,
    string? ScanMethod);
