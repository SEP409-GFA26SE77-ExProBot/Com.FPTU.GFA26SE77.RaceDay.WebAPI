namespace RaceDay.Modules.Event.Contracts.Events;

/// <summary>
/// Cross-module read for cut-off monitoring. Event owns checkpoints, cut-off strings and registrations;
/// RaceMonitoring only reads them through this contract.
/// </summary>
public interface IEventCutoffScheduleQuery
{
    /// <summary>Events whose race day falls in [fromDate, toDate] and that are not draft, rejected, cancelled or suspended.</summary>
    Task<IReadOnlyList<EventCutoffSchedule>> ListRaceDayEventsAsync(
        DateOnly fromDate,
        DateOnly toDate,
        CancellationToken cancellationToken = default);

    /// <summary>Confirmed registrations with a BIB on one event, including the race status kept by Event.</summary>
    Task<IReadOnlyList<EventRaceRunner>> ListRaceRunnersAsync(
        Guid eventId,
        CancellationToken cancellationToken = default);

    /// <summary>Order of a checkpoint on its distance (0 = first). Null when the checkpoint does not exist.</summary>
    Task<int?> GetCheckpointSortOrderAsync(
        Guid checkpointId,
        CancellationToken cancellationToken = default);
}

public sealed record EventCutoffSchedule(
    Guid EventId,
    string EventName,
    DateOnly EventDate,
    IReadOnlyList<EventCutoffDistance> Distances);

public sealed record EventCutoffDistance(
    Guid DistanceId,
    string Name,
    TimeOnly? StartAt,
    IReadOnlyList<EventCutoffCheckpoint> Checkpoints);

/// <summary>Cutoff is the raw organizer value, usually "HH:mm" local time on race day.</summary>
public sealed record EventCutoffCheckpoint(
    Guid CheckpointId,
    string Name,
    double Km,
    int SortOrder,
    string Type,
    string? Cutoff);

public sealed record EventRaceRunner(
    Guid RegistrationId,
    Guid DistanceId,
    string BibNumber,
    string RunnerName,
    string RaceStatus,
    DateTime? StartedAtUtc);
