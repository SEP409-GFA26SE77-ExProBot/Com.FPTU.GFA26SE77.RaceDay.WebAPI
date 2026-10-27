using RaceDay.Shared.Domain.Abstractions;
using RaceDay.Shared.Domain.Exceptions;

namespace RaceDay.Modules.Photo.Domain;

public sealed class PhotoDispute : Entity
{
    public Guid PhotoId { get; private set; }
    public Guid EventId { get; private set; }
    public Guid ReporterUserId { get; private set; }
    public string Reason { get; private set; } = string.Empty;
    public PhotoDisputeStatus Status { get; private set; }
    public Guid? ReviewerUserId { get; private set; }
    public string? ReviewNote { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? ReviewedAtUtc { get; private set; }

    private PhotoDispute()
    {
    }

    public static PhotoDispute Open(
        Guid photoId,
        Guid eventId,
        Guid reporterUserId,
        string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new DomainException("Lý do khiếu nại không được để trống.");
        }

        return new PhotoDispute
        {
            PhotoId = photoId,
            EventId = eventId,
            ReporterUserId = reporterUserId,
            Reason = reason.Trim(),
            Status = PhotoDisputeStatus.Open,
            CreatedAtUtc = DateTime.UtcNow
        };
    }

    public void Approve(Guid reviewerUserId, string? note = null)
    {
        EnsureOpen();
        Status = PhotoDisputeStatus.Approved;
        ReviewerUserId = reviewerUserId;
        ReviewNote = note?.Trim();
        ReviewedAtUtc = DateTime.UtcNow;
    }

    public void Reject(Guid reviewerUserId, string reason)
    {
        EnsureOpen();
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new DomainException("Lý do từ chối khiếu nại không được để trống.");
        }

        Status = PhotoDisputeStatus.Rejected;
        ReviewerUserId = reviewerUserId;
        ReviewNote = reason.Trim();
        ReviewedAtUtc = DateTime.UtcNow;
    }

    private void EnsureOpen()
    {
        if (Status != PhotoDisputeStatus.Open)
        {
            throw new DomainException("Khiếu nại không còn ở trạng thái mở.");
        }
    }
}
