using Microsoft.EntityFrameworkCore;
using RaceDay.Modules.Photo.Application.Abstractions;
using RaceDay.Modules.Photo.Domain;
using RaceDay.Modules.Photo.Infrastructure.Persistence;

namespace RaceDay.Modules.Photo.Infrastructure.Repositories;

public sealed class PhotoAssetRepository(PhotoDbContext db) : IPhotoAssetRepository
{
    private static readonly PhotoStatus[] Searchable =
    [
        PhotoStatus.Matched,
        PhotoStatus.ManualCorrected
    ];

    public Task<PhotoAsset?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        => db.Photos.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

    public Task<PhotoAsset?> GetByIdWithTagsAsync(Guid id, CancellationToken cancellationToken = default)
        => db.Photos.Include(x => x.Tags).FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

    public async Task<IReadOnlyList<PhotoAsset>> ListByEventAndStatusesAsync(
        Guid eventId,
        IReadOnlyCollection<PhotoStatus> statuses,
        int skip,
        int take,
        CancellationToken cancellationToken = default)
        => await db.Photos.AsNoTracking()
            .Include(x => x.Tags)
            .Where(x => x.EventId == eventId && statuses.Contains(x.Status))
            .OrderByDescending(x => x.CreatedAtUtc)
            .Skip(skip)
            .Take(take)
            .ToListAsync(cancellationToken);

    public Task<int> CountByEventAndStatusesAsync(
        Guid eventId,
        IReadOnlyCollection<PhotoStatus> statuses,
        CancellationToken cancellationToken = default)
        => db.Photos.AsNoTracking()
            .CountAsync(x => x.EventId == eventId && statuses.Contains(x.Status), cancellationToken);

    public async Task<IReadOnlyList<PhotoAsset>> ListPendingForProcessAsync(
        Guid eventId,
        int take,
        CancellationToken cancellationToken = default)
        => await db.Photos
            .Where(x =>
                x.EventId == eventId
                && (x.Status == PhotoStatus.Pending || x.Status == PhotoStatus.Failed))
            .OrderBy(x => x.CreatedAtUtc)
            .Take(take)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<PhotoAsset>> ListQueuedAsync(
        int take,
        CancellationToken cancellationToken = default)
        => await db.Photos
            .Where(x => x.Status == PhotoStatus.Queued)
            .OrderBy(x => x.CreatedAtUtc)
            .Take(take)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<PhotoAsset>> SearchByEventAndBibAsync(
        Guid eventId,
        string bibNormalized,
        int skip,
        int take,
        CancellationToken cancellationToken = default)
    {
        var bib = bibNormalized.Trim().ToUpperInvariant();
        return await db.Photos.AsNoTracking()
            .Include(x => x.Tags)
            .Where(x =>
                x.EventId == eventId
                && Searchable.Contains(x.Status)
                && x.Tags.Any(t => t.BibNormalized == bib))
            .OrderByDescending(x => x.CreatedAtUtc)
            .Skip(skip)
            .Take(take)
            .ToListAsync(cancellationToken);
    }

    public Task<int> CountSearchByEventAndBibAsync(
        Guid eventId,
        string bibNormalized,
        CancellationToken cancellationToken = default)
    {
        var bib = bibNormalized.Trim().ToUpperInvariant();
        return db.Photos.AsNoTracking()
            .CountAsync(x =>
                x.EventId == eventId
                && Searchable.Contains(x.Status)
                && x.Tags.Any(t => t.BibNormalized == bib),
                cancellationToken);
    }

    public Task<bool> ExistsShaAsync(
        Guid eventId,
        string contentSha256,
        CancellationToken cancellationToken = default)
        => db.Photos.AsNoTracking().AnyAsync(
            x => x.EventId == eventId
                 && x.ContentSha256 == contentSha256
                 && x.Status != PhotoStatus.Discarded,
            cancellationToken);

    public async Task AddAsync(PhotoAsset photo, CancellationToken cancellationToken = default)
        => await db.Photos.AddAsync(photo, cancellationToken);

    public void Update(PhotoAsset photo) => db.Photos.Update(photo);
}

public sealed class PhotoDisputeRepository(PhotoDbContext db) : IPhotoDisputeRepository
{
    public Task<PhotoDispute?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        => db.PhotoDisputes.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

    public async Task<IReadOnlyList<PhotoDispute>> ListOpenByEventAsync(
        Guid eventId,
        CancellationToken cancellationToken = default)
        => await db.PhotoDisputes.AsNoTracking()
            .Where(x => x.EventId == eventId && x.Status == PhotoDisputeStatus.Open)
            .OrderByDescending(x => x.CreatedAtUtc)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<PhotoDispute>> ListByReporterAsync(
        Guid reporterUserId,
        CancellationToken cancellationToken = default)
        => await db.PhotoDisputes.AsNoTracking()
            .Where(x => x.ReporterUserId == reporterUserId)
            .OrderByDescending(x => x.CreatedAtUtc)
            .ToListAsync(cancellationToken);

    public async Task AddAsync(PhotoDispute dispute, CancellationToken cancellationToken = default)
        => await db.PhotoDisputes.AddAsync(dispute, cancellationToken);

    public void Update(PhotoDispute dispute) => db.PhotoDisputes.Update(dispute);
}

public sealed class PhotoUnitOfWork(PhotoDbContext db) : IPhotoUnitOfWork
{
    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        => db.SaveChangesAsync(cancellationToken);
}
