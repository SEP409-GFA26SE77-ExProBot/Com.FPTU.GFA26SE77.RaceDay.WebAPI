using RaceDay.Modules.Photo.Domain;

namespace RaceDay.Modules.Photo.Application.Photos;

public sealed record PhotoTagDto(
    string Bib,
    double Confidence,
    string Source,
    string? BboxJson = null);

public sealed record PhotoListItemDto(
    Guid Id,
    Guid EventId,
    string Url,
    string Status,
    string? Zone,
    IReadOnlyList<PhotoTagDto> Tags,
    DateTime CreatedAtUtc,
    string? DiscardReason = null,
    bool IsPublished = false,
    DateTime? PublishedAtUtc = null);

public sealed record PhotoDetailDto(
    Guid Id,
    Guid EventId,
    string Url,
    string Status,
    string? Zone,
    string? OriginalFileName,
    string? DiscardReason,
    IReadOnlyList<PhotoTagDto> Tags,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc,
    bool IsPublished = false,
    DateTime? PublishedAtUtc = null);

public sealed record PublishPhotosResultDto(int PublishedCount);

public sealed record BulkUploadResultDto(
    int Accepted,
    int SkippedDuplicate,
    IReadOnlyList<Guid> PhotoIds);

public sealed record ProcessPhotosResultDto(
    int QueuedCount);

public sealed record PagedPhotosDto(
    IReadOnlyList<PhotoListItemDto> Items,
    int Page,
    int PageSize,
    int Total);

public static class PhotoDtoMapper
{
    public static string ToApiStatus(PhotoStatus status) => status switch
    {
        PhotoStatus.Pending => "pending",
        PhotoStatus.Queued => "queued",
        PhotoStatus.Processing => "processing",
        PhotoStatus.Matched => "matched",
        PhotoStatus.NeedsReview => "needs_review",
        PhotoStatus.ManualCorrected => "manual_corrected",
        PhotoStatus.Discarded => "discarded",
        PhotoStatus.Failed => "failed",
        PhotoStatus.RunnerReported => "runner_reported",
        _ => status.ToString().ToLowerInvariant()
    };

    public static PhotoListItemDto ToListItem(PhotoAsset p) => new(
        p.Id,
        p.EventId,
        p.Url,
        ToApiStatus(p.Status),
        p.Zone,
        p.Tags.Select(t => new PhotoTagDto(
            t.BibNormalized,
            t.Confidence,
            t.Source == PhotoTagSource.Manual ? "manual" : "ocr",
            t.BboxJson)).ToList(),
        p.CreatedAtUtc,
        p.DiscardReason,
        p.IsPublished,
        p.PublishedAtUtc);

    public static PhotoDetailDto ToDetail(PhotoAsset p) => new(
        p.Id,
        p.EventId,
        p.Url,
        ToApiStatus(p.Status),
        p.Zone,
        p.OriginalFileName,
        p.DiscardReason,
        p.Tags.Select(t => new PhotoTagDto(
            t.BibNormalized,
            t.Confidence,
            t.Source == PhotoTagSource.Manual ? "manual" : "ocr",
            t.BboxJson)).ToList(),
        p.CreatedAtUtc,
        p.UpdatedAtUtc,
        p.IsPublished,
        p.PublishedAtUtc);
}
