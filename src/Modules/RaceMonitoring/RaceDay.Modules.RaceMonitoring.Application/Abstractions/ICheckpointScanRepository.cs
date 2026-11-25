using RaceDay.Modules.RaceMonitoring.Domain;

namespace RaceDay.Modules.RaceMonitoring.Application.Abstractions;

public interface ICheckpointScanRepository
{
    Task<CheckpointScan?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CheckpointPassageInfo>> ListFirstPassagesAsync(
        Guid checkpointId,
        IReadOnlyCollection<Guid> registrationIds,
        CancellationToken cancellationToken = default);

    /// <summary>First scan per (registration, checkpoint) over the given checkpoints.</summary>
    Task<IReadOnlyList<CheckpointFirstPassage>> ListFirstPassagesByCheckpointsAsync(
        IReadOnlyCollection<Guid> checkpointIds,
        CancellationToken cancellationToken = default);

    /// <summary>Newest scans of one registration first.</summary>
    Task<IReadOnlyList<CheckpointScan>> ListByRegistrationAsync(
        Guid registrationId,
        int take,
        CancellationToken cancellationToken = default);

    void Add(CheckpointScan scan);
}

public sealed record CheckpointPassageInfo(
    Guid RegistrationId,
    DateTime PassageTime);

public sealed record CheckpointFirstPassage(
    Guid RegistrationId,
    Guid CheckpointId,
    DateTime PassedAtUtc);
