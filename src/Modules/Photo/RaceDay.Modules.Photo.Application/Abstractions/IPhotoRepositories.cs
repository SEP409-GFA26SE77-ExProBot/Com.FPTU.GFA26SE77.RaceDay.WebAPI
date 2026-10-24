using RaceDay.Modules.Photo.Domain;
using RaceDay.Shared.Application.Abstractions;

namespace RaceDay.Modules.Photo.Application.Abstractions;

public interface IPhotoUnitOfWork : IUnitOfWork;

public interface IPhotoAssetRepository
{
    Task<PhotoAsset?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<PhotoAsset?> GetByIdWithTagsAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<PhotoAsset>> ListByEventAndStatusesAsync(
        Guid eventId,
        IReadOnlyCollection<PhotoStatus> statuses,
        int skip,
        int take,
        CancellationToken cancellationToken = default);
    Task<int> CountByEventAndStatusesAsync(
        Guid eventId,
        IReadOnlyCollection<PhotoStatus> statuses,
        CancellationToken cancellationToken = default);
    Task<IReadOnlyList<PhotoAsset>> ListPendingForProcessAsync(
        Guid eventId,
        int take,
        CancellationToken cancellationToken = default);
    Task<IReadOnlyList<PhotoAsset>> ListQueuedAsync(
        int take,
        CancellationToken cancellationToken = default);
    Task<IReadOnlyList<PhotoAsset>> SearchByEventAndBibAsync(
        Guid eventId,
        string bibNormalized,
        int skip,
        int take,
        CancellationToken cancellationToken = default);
    Task<int> CountSearchByEventAndBibAsync(
        Guid eventId,
        string bibNormalized,
        CancellationToken cancellationToken = default);
    Task<bool> ExistsShaAsync(
        Guid eventId,
        string contentSha256,
        CancellationToken cancellationToken = default);
    Task AddAsync(PhotoAsset photo, CancellationToken cancellationToken = default);
    void Update(PhotoAsset photo);
}

public interface IPhotoDisputeRepository
{
    Task<PhotoDispute?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<PhotoDispute>> ListOpenByEventAsync(
        Guid eventId,
        CancellationToken cancellationToken = default);
    Task<IReadOnlyList<PhotoDispute>> ListByReporterAsync(
        Guid reporterUserId,
        CancellationToken cancellationToken = default);
    Task AddAsync(PhotoDispute dispute, CancellationToken cancellationToken = default);
    void Update(PhotoDispute dispute);
}

public interface IOcrClient
{
    bool IsConfigured { get; }

    Task<string> EnqueueJobAsync(
        Guid jobId,
        Guid eventId,
        Guid photoId,
        string imageUrl,
        string bibRosterVersion,
        IReadOnlyList<string>? rosterBibs,
        CancellationToken cancellationToken = default);
}
