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

public static class RaceEventLogNotificationKinds
{
    /// <summary>A new timeline row (SSE RaceEventLogCreated).</summary>
    public const string Created = "Created";

    /// <summary>
    /// Simulated rows of a distance were removed except those of <see cref="RaceEventLogNotification.SimulationRunId"/>
    /// (SSE RaceEventLogsCleared). Only EventId, EventDistanceId and SimulationRunId are meaningful.
    /// </summary>
    public const string SimulationCleared = "SimulationCleared";
}

/// <summary>
/// <see cref="ChipStartAtUtc"/>: the runner's first pass at a Start checkpoint (chip time origin), null when unknown.
/// <see cref="Simulated"/> rows come from the race simulator and carry <see cref="SimulationRunId"/>.
/// </summary>
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
    string? ScanMethod,
    string? CheckpointName = null,
    string? RunnerName = null,
    DateTime? ChipStartAtUtc = null,
    bool Simulated = false,
    Guid? SimulationRunId = null,
    string Kind = RaceEventLogNotificationKinds.Created)
{
    public static RaceEventLogNotification SimulationCleared(Guid eventId, Guid distanceId, Guid currentRunId)
    {
        var now = DateTime.UtcNow;
        return new RaceEventLogNotification(
            Guid.NewGuid(), 0, eventId, distanceId, RaceEventLogNotificationKinds.SimulationCleared, now, now,
            null, null, null, null, null, null,
            SimulationRunId: currentRunId,
            Kind: RaceEventLogNotificationKinds.SimulationCleared);
    }
}
