using Microsoft.EntityFrameworkCore;
using RaceDay.Modules.RaceMonitoring.Contracts.Timeline;
using RaceDay.Modules.RaceMonitoring.Infrastructure.Persistence;

namespace RaceDay.Modules.RaceMonitoring.Infrastructure.RaceEventLogs;

/// <summary>A checkpoint has activity when it has a scan or a timeline row (simulated rows included).</summary>
public sealed class CheckpointActivityQuery(RaceMonitoringDbContext db) : ICheckpointActivityQuery
{
    public async Task<IReadOnlyCollection<Guid>> ListCheckpointIdsWithActivityAsync(
        IReadOnlyCollection<Guid> checkpointIds,
        CancellationToken cancellationToken = default)
    {
        if (checkpointIds.Count == 0)
        {
            return [];
        }

        var ids = checkpointIds.ToList();
        var nullableIds = checkpointIds.Cast<Guid?>().ToList();
        var scanned = await db.CheckpointScans.AsNoTracking()
            .Where(scan => ids.Contains(scan.CheckpointId))
            .Select(scan => scan.CheckpointId)
            .Distinct()
            .ToListAsync(cancellationToken);
        var logged = await db.RaceEventLogs.AsNoTracking()
            .Where(log => nullableIds.Contains(log.CheckpointId))
            .Select(log => log.CheckpointId!.Value)
            .Distinct()
            .ToListAsync(cancellationToken);
        return scanned.Union(logged).ToList();
    }
}
