using RaceDay.Shared.Domain.Abstractions;
using RaceDay.Shared.Domain.Exceptions;

namespace RaceDay.Modules.Photo.Domain;

public sealed class PhotoAsset : AggregateRoot
{
    private readonly List<PhotoTag> _tags = [];

    public Guid EventId { get; private set; }
    public Guid UploadedByUserId { get; private set; }
    public string Url { get; private set; } = string.Empty;
    public string? ContentSha256 { get; private set; }
    public string? OriginalFileName { get; private set; }
    public string? ContentType { get; private set; }
    public long? SizeBytes { get; private set; }
    public string? Zone { get; private set; }
    public PhotoStatus Status { get; private set; }
    public string? DiscardReason { get; private set; }
    public string? OcrJobId { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }

    public IReadOnlyCollection<PhotoTag> Tags => _tags.AsReadOnly();

    private PhotoAsset()
    {
    }

    public static PhotoAsset CreatePending(
        Guid eventId,
        Guid uploadedByUserId,
        string url,
        string? contentSha256,
        string? originalFileName,
        string? contentType,
        long? sizeBytes,
        string? zone = null)
    {
        if (eventId == Guid.Empty)
        {
            throw new DomainException("Thiếu eventId.");
        }

        if (string.IsNullOrWhiteSpace(url))
        {
            throw new DomainException("Thiếu URL ảnh.");
        }

        var now = DateTime.UtcNow;
        return new PhotoAsset
        {
            EventId = eventId,
            UploadedByUserId = uploadedByUserId,
            Url = url.Trim(),
            ContentSha256 = contentSha256?.Trim().ToLowerInvariant(),
            OriginalFileName = originalFileName?.Trim(),
            ContentType = contentType?.Trim(),
            SizeBytes = sizeBytes,
            Zone = zone?.Trim(),
            Status = PhotoStatus.Pending,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };
    }

    public void MarkQueued(string? ocrJobId = null)
    {
        if (Status is not (PhotoStatus.Pending or PhotoStatus.Failed or PhotoStatus.Queued))
        {
            throw new DomainException("Chỉ ảnh pending/failed mới đưa vào hàng đợi OCR.");
        }

        Status = PhotoStatus.Queued;
        OcrJobId = ocrJobId;
        Touch();
    }

    /// <summary>
    /// Worker chết / timeout giữa OCR → Processing kẹt mãi. Cho phép reclaim về Queued.
    /// </summary>
    public void RequeueStuckProcessing()
    {
        if (Status is not PhotoStatus.Processing)
        {
            throw new DomainException("Chỉ ảnh đang Processing mới reclaim về hàng đợi.");
        }

        Status = PhotoStatus.Queued;
        OcrJobId = null;
        Touch();
    }

    public void MarkProcessing(string? ocrJobId = null)
    {
        if (Status is not (PhotoStatus.Queued or PhotoStatus.Pending or PhotoStatus.Processing))
        {
            throw new DomainException("Ảnh không ở trạng thái chờ xử lý.");
        }

        Status = PhotoStatus.Processing;
        if (!string.IsNullOrWhiteSpace(ocrJobId))
        {
            OcrJobId = ocrJobId.Trim();
        }

        Touch();
    }

    public void ApplyOcrMatched(IReadOnlyList<(string Bib, double Confidence, string? BboxJson)> tags)
    {
        ReplaceOcrTags(tags);
        Status = PhotoStatus.Matched;
        DiscardReason = null;
        Touch();
    }

    public void ApplyOcrNeedsReview(IReadOnlyList<(string Bib, double Confidence, string? BboxJson)> tags)
    {
        ReplaceOcrTags(tags);
        Status = PhotoStatus.NeedsReview;
        DiscardReason = null;
        Touch();
    }

    public void Discard(string reason)
    {
        if (Status is PhotoStatus.Matched or PhotoStatus.ManualCorrected)
        {
            throw new DomainException(
                "Ảnh đã khớp không discard tại đây — dùng khiếu nại/review workflow.");
        }

        _tags.Clear();
        Status = PhotoStatus.Discarded;
        DiscardReason = string.IsNullOrWhiteSpace(reason) ? "discarded" : reason.Trim();
        Touch();
    }

    public void MarkFailed(string reason)
    {
        Status = PhotoStatus.Failed;
        DiscardReason = reason.Trim();
        Touch();
    }

    public void CorrectBib(string bibNormalized)
    {
        if (Status is not (PhotoStatus.NeedsReview or PhotoStatus.RunnerReported))
        {
            throw new DomainException("Chỉ ảnh cần review / bị report mới được sửa BIB.");
        }

        var bib = bibNormalized.Trim().ToUpperInvariant();
        _tags.Clear();
        _tags.Add(PhotoTag.Create(Id, bib, 1.0, PhotoTagSource.Manual));
        Status = PhotoStatus.ManualCorrected;
        DiscardReason = null;
        Touch();
    }

    public void MarkRunnerReported()
    {
        if (Status is not (PhotoStatus.Matched or PhotoStatus.ManualCorrected))
        {
            throw new DomainException("Chỉ ảnh đã khớp mới nhận khiếu nại runner.");
        }

        Status = PhotoStatus.RunnerReported;
        Touch();
    }

    public void RestoreAfterDisputeApproved()
    {
        if (Status != PhotoStatus.RunnerReported)
        {
            return;
        }

        Status = _tags.Any(t => t.Source == PhotoTagSource.Manual)
            ? PhotoStatus.ManualCorrected
            : PhotoStatus.Matched;
        Touch();
    }

    public bool IsSearchable =>
        Status is PhotoStatus.Matched or PhotoStatus.ManualCorrected;

    private void ReplaceOcrTags(IReadOnlyList<(string Bib, double Confidence, string? BboxJson)> tags)
    {
        _tags.Clear();
        foreach (var t in tags)
        {
            _tags.Add(PhotoTag.Create(Id, t.Bib, t.Confidence, PhotoTagSource.Ocr, t.BboxJson));
        }
    }

    private void Touch() => UpdatedAtUtc = DateTime.UtcNow;
}
