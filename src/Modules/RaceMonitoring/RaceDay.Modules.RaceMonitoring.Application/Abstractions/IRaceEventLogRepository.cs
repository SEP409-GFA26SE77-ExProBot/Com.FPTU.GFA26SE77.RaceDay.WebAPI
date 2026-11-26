using RaceDay.Modules.RaceMonitoring.Domain;

namespace RaceDay.Modules.RaceMonitoring.Application.Abstractions;

public interface IRaceEventLogRepository
{
    Task<RaceEventLog?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<RaceEventLog?> FindBySourceAsync(
        string sourceType,
        Guid sourceId,
        CancellationToken cancellationToken = default);

    Task<(IReadOnlyList<RaceEventLog> Items, int Total)> ListAsync(
        RaceEventLogFilter filter,
        CancellationToken cancellationToken = default);

    IAsyncEnumerable<RaceEventLog> ReplayAsync(
        Guid eventId,
        long afterSequence,
        DateTime recordedAfterUtc,
        CancellationToken cancellationToken = default);

    void Add(RaceEventLog log);

    /// <summary>
    /// First RunnerCheckpointPassed time per (registration, simulated) at any of <paramref name="startCheckpointIds"/>.
    /// Simulated and real starts are kept apart so a simulated run never borrows a real start, or the reverse.
    /// </summary>
    Task<IReadOnlyList<RaceEventLogChipStart>> ListChipStartsAsync(
        Guid eventId,
        IReadOnlyCollection<Guid> startCheckpointIds,
        IReadOnlyCollection<Guid> registrationIds,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyCollection<Guid>> ListExistingSourceIdsAsync(
        string sourceType,
        IReadOnlyCollection<Guid> sourceIds,
        CancellationToken cancellationToken = default);

    /// <summary>Deletes simulated rows of one distance that do not belong to <paramref name="currentRunId"/>.</summary>
    Task<int> DeleteOtherSimulationRunsAsync(
        Guid eventId,
        Guid distanceId,
        Guid currentRunId,
        CancellationToken cancellationToken = default);
}

public sealed record RaceEventLogChipStart(Guid RegistrationId, bool Simulated, DateTime StartedAtUtc);

public sealed record RaceEventLogFilter(
    Guid EventId,
    Guid? EventDistanceId,
    RaceEventLogType? EventType,
    string? BibNumber,
    DateTime? FromUtc,
    DateTime? ToUtc,
    long? AfterSequence,
    int Page,
    int PageSize);
