using Microsoft.EntityFrameworkCore;
using RaceDay.Modules.RaceMonitoring.Application.Abstractions;
using RaceDay.Modules.RaceMonitoring.Application.RaceEventLogs;
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

    public async Task<IReadOnlyList<RaceEventLogChipStart>> ListChipStartsAsync(
        Guid eventId,
        IReadOnlyCollection<Guid> startCheckpointIds,
        IReadOnlyCollection<Guid> registrationIds,
        CancellationToken cancellationToken = default)
    {
        if (startCheckpointIds.Count == 0 || registrationIds.Count == 0)
        {
            return [];
        }

        var checkpoints = startCheckpointIds.Cast<Guid?>().ToList();
        var registrations = registrationIds.Cast<Guid?>().ToList();
        var rows = await db.RaceEventLogs.AsNoTracking()
            .Where(log => log.EventId == eventId
                && log.EventType == RaceEventLogType.RunnerCheckpointPassed
                && checkpoints.Contains(log.CheckpointId)
                && registrations.Contains(log.RegistrationId))
            .GroupBy(log => new { log.RegistrationId, Simulated = log.SourceType == RaceEventLogSource.Simulation })
            .Select(group => new
            {
                group.Key.RegistrationId,
                group.Key.Simulated,
                StartedAtUtc = group.Min(log => log.OccurredAtUtc)
            })
            .ToListAsync(cancellationToken);
        return rows
            .Where(row => row.RegistrationId.HasValue)
            .Select(row => new RaceEventLogChipStart(row.RegistrationId!.Value, row.Simulated, row.StartedAtUtc))
            .ToList();
    }

    public async Task<IReadOnlyCollection<Guid>> ListExistingSourceIdsAsync(
        string sourceType,
        IReadOnlyCollection<Guid> sourceIds,
        CancellationToken cancellationToken = default)
    {
        if (sourceIds.Count == 0)
        {
            return [];
        }

        var ids = sourceIds.Cast<Guid?>().ToList();
        var found = await db.RaceEventLogs.AsNoTracking()
            .Where(log => log.SourceType == sourceType && ids.Contains(log.SourceId))
            .Select(log => log.SourceId)
            .ToListAsync(cancellationToken);
        return found.Where(id => id.HasValue).Select(id => id!.Value).ToList();
    }

    public Task<int> DeleteOtherSimulationRunsAsync(
        Guid eventId,
        Guid distanceId,
        Guid currentRunId,
        CancellationToken cancellationToken = default)
    {
        var current = RaceEventLogMapper.SimulationRunIdContainment(currentRunId);
        return db.RaceEventLogs
            .Where(log => log.EventId == eventId
                && log.EventDistanceId == distanceId
                && log.SourceType == RaceEventLogSource.Simulation
                && !(log.Metadata != null && EF.Functions.JsonContains(log.Metadata, current)))
            .ExecuteDeleteAsync(cancellationToken);
    }

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
