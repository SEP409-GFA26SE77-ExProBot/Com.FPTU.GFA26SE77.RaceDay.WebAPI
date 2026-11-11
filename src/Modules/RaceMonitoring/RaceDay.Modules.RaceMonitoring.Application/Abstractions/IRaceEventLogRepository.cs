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
}

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
