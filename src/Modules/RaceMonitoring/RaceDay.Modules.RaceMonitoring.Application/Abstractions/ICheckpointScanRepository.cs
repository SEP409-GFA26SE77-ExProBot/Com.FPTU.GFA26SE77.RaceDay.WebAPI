using RaceDay.Modules.RaceMonitoring.Domain;

namespace RaceDay.Modules.RaceMonitoring.Application.Abstractions;

public interface ICheckpointScanRepository
{
    Task<CheckpointScan?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CheckpointPassageInfo>> ListFirstPassagesAsync(
        Guid checkpointId,
        IReadOnlyCollection<Guid> registrationIds,
        CancellationToken cancellationToken = default);

    void Add(CheckpointScan scan);
}

public sealed record CheckpointPassageInfo(
    Guid RegistrationId,
    DateTime PassageTime);
