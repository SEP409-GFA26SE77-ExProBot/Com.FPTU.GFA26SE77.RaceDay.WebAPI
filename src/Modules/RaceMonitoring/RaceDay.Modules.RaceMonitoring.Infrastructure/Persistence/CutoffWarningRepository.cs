using Microsoft.EntityFrameworkCore;
using RaceDay.Modules.RaceMonitoring.Application.Abstractions;
using RaceDay.Modules.RaceMonitoring.Domain;
using RaceDay.Modules.RaceMonitoring.Domain.RunnerMonitoring;

namespace RaceDay.Modules.RaceMonitoring.Infrastructure.Persistence;

public sealed class CutoffWarningRepository(RaceMonitoringDbContext db) : ICutoffWarningRepository
{
    public Task<CutoffWarning?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        => db.CutoffWarnings.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

    public Task<CutoffWarning?> FindAsync(Guid id, CancellationToken cancellationToken = default)
        => db.CutoffWarnings.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

    public async Task<IReadOnlyList<CutoffWarningKey>> ListKeysByEventAsync(
        Guid eventId,
        CancellationToken cancellationToken = default)
        => await db.CutoffWarnings.AsNoTracking()
            .Where(x => x.EventId == eventId)
            .Select(x => new CutoffWarningKey(x.RegistrationId, x.CheckpointId))
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<CutoffWarning>> ListActiveByEventAsync(
        Guid eventId,
        CancellationToken cancellationToken = default)
        => await db.CutoffWarnings
            .Where(x => x.EventId == eventId && x.Status == CutoffWarningStatus.Active)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<CutoffWarning>> ListActiveByRegistrationAsync(
        Guid registrationId,
        CancellationToken cancellationToken = default)
        => await db.CutoffWarnings
            .Where(x => x.RegistrationId == registrationId && x.Status == CutoffWarningStatus.Active)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<CutoffWarningEventSummary>> SummarizeEventsAsync(
        CancellationToken cancellationToken = default)
    {
        var rows = await db.CutoffWarnings.AsNoTracking()
            .GroupBy(x => x.EventId)
            .Select(g => new
            {
                EventId = g.Key,
                EventName = g.Max(x => x.EventName),
                ActiveCount = g.Count(x => x.Status == CutoffWarningStatus.Active),
                TotalCount = g.Count(),
                LastDetectedAtUtc = g.Max(x => x.DetectedAtUtc)
            })
            .ToListAsync(cancellationToken);

        return rows
            .OrderByDescending(x => x.ActiveCount > 0)
            .ThenByDescending(x => x.LastDetectedAtUtc)
            .Select(x => new CutoffWarningEventSummary(
                x.EventId,
                x.EventName ?? string.Empty,
                x.ActiveCount,
                x.TotalCount,
                x.LastDetectedAtUtc))
            .ToArray();
    }

    public async Task<CutoffWarningPage> ListAsync(CutoffWarningFilter filter, CancellationToken cancellationToken = default)
    {
        if (filter.EventIds.Count == 0)
        {
            return new CutoffWarningPage([], 0);
        }

        var eventIds = filter.EventIds.ToArray();
        var query = db.CutoffWarnings.AsNoTracking().Where(x => eventIds.Contains(x.EventId));
        if (filter.Status is { } status)
        {
            query = query.Where(x => x.Status == status);
        }

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var pattern = "%" + EscapeLike(filter.Search.Trim()) + "%";
            query = query.Where(x =>
                EF.Functions.ILike(x.BibNumber, pattern, "\\")
                || EF.Functions.ILike(x.RunnerName, pattern, "\\")
                || EF.Functions.ILike(x.CheckpointName, pattern, "\\"));
        }

        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderBy(x => x.Status == CutoffWarningStatus.Active ? 0 : 1)
            .ThenByDescending(x => x.DetectedAtUtc)
            .ThenBy(x => x.BibNumber)
            .Skip((filter.Page - 1) * filter.PageSize)
            .Take(filter.PageSize)
            .ToListAsync(cancellationToken);
        return new CutoffWarningPage(items, total);
    }

    public void Add(CutoffWarning warning) => db.CutoffWarnings.Add(warning);

    private static string EscapeLike(string value)
        => value.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
}
