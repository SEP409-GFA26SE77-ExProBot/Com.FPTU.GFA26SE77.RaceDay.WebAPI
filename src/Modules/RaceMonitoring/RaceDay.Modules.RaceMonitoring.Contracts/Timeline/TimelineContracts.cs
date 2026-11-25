namespace RaceDay.Modules.RaceMonitoring.Contracts.Timeline;

/// <summary>
/// Cross-module read: which event checkpoints already have race data (scans or timeline rows).
/// Event uses it to refuse deleting such a checkpoint when the organizer saves the map course.
/// </summary>
public interface ICheckpointActivityQuery
{
    Task<IReadOnlyCollection<Guid>> ListCheckpointIdsWithActivityAsync(
        IReadOnlyCollection<Guid> checkpointIds,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// One checkpoint crossing of a simulated runner. <see cref="CourseCheckpointId"/> is the map-course id
/// (event_checkpoints.ExternalId); <see cref="SimSeconds"/> is the interpolated crossing time on the run clock.
/// </summary>
public sealed record SimulatedCheckpointPassage(
    Guid RunId,
    Guid EventId,
    Guid DistanceId,
    Guid RegistrationId,
    string Bib,
    string RunnerName,
    string CourseCheckpointId,
    string CheckpointName,
    double SimSeconds);

/// <summary>
/// Simulation → timeline. Rows are written as RFID passes marked as simulated, without checkpoint scans,
/// so cut-off monitoring never sees them. <see cref="ClearOtherRunsAsync"/> is called when a run starts on a
/// distance (create, Reset, New Run, or after an API restart) and removes every other simulated run's rows there.
/// </summary>
public interface ISimulationTimelineSink
{
    Task RecordAsync(
        IReadOnlyList<SimulatedCheckpointPassage> passages,
        CancellationToken cancellationToken = default);

    Task ClearOtherRunsAsync(
        Guid eventId,
        Guid distanceId,
        Guid currentRunId,
        CancellationToken cancellationToken = default);
}
