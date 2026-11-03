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
        CancellationToken cancellationToken = default,
        bool? isPublished = null);
    Task<int> CountByEventAndStatusesAsync(
        Guid eventId,
        IReadOnlyCollection<PhotoStatus> statuses,
        CancellationToken cancellationToken = default,
        bool? isPublished = null);
    Task<IReadOnlyList<PhotoAsset>> ListPendingForProcessAsync(
        Guid eventId,
        int take,
        CancellationToken cancellationToken = default);
    Task<IReadOnlyList<PhotoAsset>> ListQueuedAsync(
        int take,
        CancellationToken cancellationToken = default);

    /// <summary>Processing lâu hơn threshold — thường do worker restart / OCR timeout.</summary>
    Task<IReadOnlyList<PhotoAsset>> ListStuckProcessingAsync(
        TimeSpan olderThan,
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

    /// <summary>Hard-delete tags then reload aggregate (avoids EF collection concurrency on OCR apply).</summary>
    Task<PhotoAsset?> ReloadForOcrApplyAsync(Guid photoId, CancellationToken cancellationToken = default);
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

public sealed record OcrEnqueueTag(string Bib, double Confidence, IReadOnlyList<double>? Bbox);

/// <summary>
/// OCR service may return the full result synchronously (standalone mode)
/// or only acknowledge the job (async + callback later).
/// </summary>
public sealed record OcrEnqueueResult(
    string RemoteJobId,
    string? Status = null,
    string? DuplicateOf = null,
    IReadOnlyList<OcrEnqueueTag>? Tags = null,
    IReadOnlyList<string>? Reasons = null,
    bool HasInlineResult = false);

public interface IOcrClient
{
    bool IsConfigured { get; }

    Task<OcrEnqueueResult> EnqueueJobAsync(
        Guid jobId,
        Guid eventId,
        Guid photoId,
        string imageUrl,
        string bibRosterVersion,
        IReadOnlyList<string>? rosterBibs,
        CancellationToken cancellationToken = default);
}

/// <summary>Server-side fetch ảnh (bypass CORS trình duyệt) để runner tải về.</summary>
public interface IPhotoContentFetcher
{
    Task<PhotoContentBytes> FetchAsync(string fileUrl, CancellationToken cancellationToken = default);
}

public sealed record PhotoContentBytes(
    byte[] Content,
    string ContentType);
