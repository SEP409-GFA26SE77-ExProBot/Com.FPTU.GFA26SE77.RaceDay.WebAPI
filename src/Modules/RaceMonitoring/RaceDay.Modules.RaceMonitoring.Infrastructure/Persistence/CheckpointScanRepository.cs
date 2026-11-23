using Microsoft.EntityFrameworkCore;
using RaceDay.Modules.RaceMonitoring.Application.Abstractions;
using RaceDay.Modules.RaceMonitoring.Domain;

namespace RaceDay.Modules.RaceMonitoring.Infrastructure.Persistence;

public sealed class CheckpointScanRepository(RaceMonitoringDbContext db) : ICheckpointScanRepository
{
    public Task<CheckpointScan?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        => db.CheckpointScans.AsNoTracking().FirstOrDefaultAsync(scan => scan.Id == id, cancellationToken);

    public async Task<IReadOnlyList<CheckpointPassageInfo>> ListFirstPassagesAsync(
        Guid checkpointId,
        IReadOnlyCollection<Guid> registrationIds,
        CancellationToken cancellationToken = default)
    {
        if (registrationIds.Count == 0)
        {
            return [];
        }

        var ids = registrationIds.Distinct().ToArray();

        return await db.CheckpointScans.AsNoTracking()
            .Where(scan => scan.CheckpointId == checkpointId && ids.Contains(scan.RegistrationId))
            .GroupBy(scan => scan.RegistrationId)
            .Select(group => new CheckpointPassageInfo(
                group.Key,
                group.Min(scan => scan.ScannedAtUtc)))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<CheckpointFirstPassage>> ListFirstPassagesByCheckpointsAsync(
        IReadOnlyCollection<Guid> checkpointIds,
        CancellationToken cancellationToken = default)
    {
        if (checkpointIds.Count == 0)
        {
            return [];
        }

        var ids = checkpointIds.Distinct().ToArray();
        return await db.CheckpointScans.AsNoTracking()
            .Where(scan => ids.Contains(scan.CheckpointId))
            .GroupBy(scan => new { scan.RegistrationId, scan.CheckpointId })
            .Select(group => new CheckpointFirstPassage(
                group.Key.RegistrationId,
                group.Key.CheckpointId,
                group.Min(scan => scan.ScannedAtUtc)))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<CheckpointScan>> ListByRegistrationAsync(
        Guid registrationId,
        int take,
        CancellationToken cancellationToken = default)
        => await db.CheckpointScans.AsNoTracking()
            .Where(scan => scan.RegistrationId == registrationId)
            .OrderByDescending(scan => scan.ScannedAtUtc)
            .Take(take)
            .ToListAsync(cancellationToken);

    public void Add(CheckpointScan scan) => db.CheckpointScans.Add(scan);
}
