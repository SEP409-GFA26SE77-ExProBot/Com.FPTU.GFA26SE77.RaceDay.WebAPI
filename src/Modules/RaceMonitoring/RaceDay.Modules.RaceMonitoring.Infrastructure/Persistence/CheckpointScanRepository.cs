using Microsoft.EntityFrameworkCore;
using RaceDay.Modules.RaceMonitoring.Application.Abstractions;
using RaceDay.Modules.RaceMonitoring.Domain;

namespace RaceDay.Modules.RaceMonitoring.Infrastructure.Persistence;

public sealed class CheckpointScanRepository(RaceMonitoringDbContext db) : ICheckpointScanRepository
{
    public Task<CheckpointScan?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        => db.CheckpointScans.AsNoTracking().FirstOrDefaultAsync(scan => scan.Id == id, cancellationToken);

    public void Add(CheckpointScan scan) => db.CheckpointScans.Add(scan);
}
