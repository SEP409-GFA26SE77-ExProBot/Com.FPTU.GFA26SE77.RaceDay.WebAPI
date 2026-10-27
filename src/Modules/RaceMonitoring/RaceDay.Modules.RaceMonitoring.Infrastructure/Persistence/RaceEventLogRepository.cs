using Microsoft.EntityFrameworkCore;
using RaceDay.Modules.RaceMonitoring.Application.Abstractions;
using RaceDay.Modules.RaceMonitoring.Domain;

namespace RaceDay.Modules.RaceMonitoring.Infrastructure.Persistence;

public sealed class RaceEventLogRepository(RaceMonitoringDbContext db) : IRaceEventLogRepository
{
    public Task<RaceEventLog?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        => db.RaceEventLogs.AsNoTracking().FirstOrDefaultAsync(log => log.Id == id, cancellationToken);

    public Task<RaceEventLog?> FindBySourceAsync(
        string sourceType,
        Guid sourceId,
        CancellationToken cancellationToken = default)
        => db.RaceEventLogs.AsNoTracking().FirstOrDefaultAsync(
            log => log.SourceType == sourceType && log.SourceId == sourceId,
            cancellationToken);

    public async Task<(IReadOnlyList<RaceEventLog> Items, int Total)> ListAsync(
        RaceEventLogFilter filter,
        CancellationToken cancellationToken = default)
    {
        var query = Apply(db.RaceEventLogs.AsNoTracking(), filter);
        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderBy(log => log.OccurredAtUtc)
            .ThenBy(log => log.Sequence)
            .Skip((filter.Page - 1) * filter.PageSize)
            .Take(filter.PageSize)
            .ToListAsync(cancellationToken);
        return (items, total);
    }

    public IAsyncEnumerable<RaceEventLog> ReplayAsync(
        Guid eventId,
        long afterSequence,
        DateTime recordedAfterUtc,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return db.RaceEventLogs.AsNoTracking()
            .Where(log => log.EventId == eventId
                && (log.Sequence > afterSequence
                    || (afterSequence > 0 && log.Sequence <= afterSequence && log.RecordedAtUtc >= recordedAfterUtc)))
            .OrderBy(log => log.Sequence)
            .AsAsyncEnumerable();
    }

    public void Add(RaceEventLog log) => db.RaceEventLogs.Add(log);

    private static IQueryable<RaceEventLog> Apply(IQueryable<RaceEventLog> query, RaceEventLogFilter filter)
    {
        query = query.Where(log => log.EventId == filter.EventId);
        if (filter.EventDistanceId is Guid distanceId)
        {
            query = query.Where(log => log.EventDistanceId == distanceId);
        }

        if (filter.EventType is RaceEventLogType eventType)
        {
            query = query.Where(log => log.EventType == eventType);
        }

        if (!string.IsNullOrWhiteSpace(filter.BibNumber))
        {
            var bib = filter.BibNumber.Trim().ToLower();
            query = query.Where(log => log.BibNumber != null && log.BibNumber.ToLower() == bib);
        }

        if (filter.FromUtc is DateTime fromUtc)
        {
            query = query.Where(log => log.OccurredAtUtc >= fromUtc);
        }

        if (filter.ToUtc is DateTime toUtc)
        {
            query = query.Where(log => log.OccurredAtUtc <= toUtc);
        }

        if (filter.AfterSequence is long afterSequence)
        {
            query = query.Where(log => log.Sequence > afterSequence);
        }

        return query;
    }
}
