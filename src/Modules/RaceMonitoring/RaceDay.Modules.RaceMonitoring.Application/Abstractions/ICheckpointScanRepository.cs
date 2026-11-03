using RaceDay.Modules.RaceMonitoring.Domain;

namespace RaceDay.Modules.RaceMonitoring.Application.Abstractions;

public interface ICheckpointScanRepository
{
    Task<CheckpointScan?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    void Add(CheckpointScan scan);
}
