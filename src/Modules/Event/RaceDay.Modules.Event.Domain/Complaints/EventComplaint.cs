using System;
using System.Collections.Generic;
using System.Text.Json;
using RaceDay.Shared.Domain.Abstractions;
using RaceDay.Shared.Domain.Exceptions;

namespace RaceDay.Modules.Event.Domain.Complaints;

public enum ComplaintStatus
{
    Pending = 0,
    Accepted = 1,
    Rejected = 2
}

public sealed class EventComplaint : AggregateRoot
{
    public Guid EventId { get; private set; }
    public Guid DistanceId { get; private set; }
    public Guid UserId { get; private set; }
    public string Title { get; private set; } = string.Empty;
    public string Content { get; private set; } = string.Empty;
    public string EvidenceLinksJson { get; private set; } = "[]";
    public string? ResponseContent { get; private set; }
    public ComplaintStatus Status { get; private set; } = ComplaintStatus.Pending;
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? RepliedAtUtc { get; private set; }
    public Guid? RepliedByUserId { get; private set; }

    private EventComplaint() { }

    public static EventComplaint Create(
        Guid eventId,
        Guid distanceId,
        Guid userId,
        string title,
        string content,
        IEnumerable<string>? evidenceLinks)
    {
        if (eventId == Guid.Empty)
        {
            throw new DomainException("Giải đấu không hợp lệ.");
        }

        if (distanceId == Guid.Empty)
        {
            throw new DomainException("Cự ly không hợp lệ.");
        }

        if (userId == Guid.Empty)
        {
            throw new DomainException("Thông tin người dùng không hợp lệ.");
        }

        if (string.IsNullOrWhiteSpace(title))
        {
            throw new DomainException("Tiêu đề khiếu nại không được để trống.");
        }

        if (string.IsNullOrWhiteSpace(content))
        {
            throw new DomainException("Nội dung khiếu nại không được để trống.");
        }

        var links = evidenceLinks != null ? new List<string>(evidenceLinks) : new List<string>();

        return new EventComplaint
        {
            Id = Guid.NewGuid(),
            EventId = eventId,
            DistanceId = distanceId,
            UserId = userId,
            Title = title.Trim(),
            Content = content.Trim(),
            EvidenceLinksJson = JsonSerializer.Serialize(links),
            Status = ComplaintStatus.Pending,
            CreatedAtUtc = DateTime.UtcNow
        };
    }

    public void Reply(Guid organizerUserId, string responseContent, ComplaintStatus decision)
    {
        if (organizerUserId == Guid.Empty)
        {
            throw new DomainException("Thông tin Ban tổ chức không hợp lệ.");
        }

        if (Status != ComplaintStatus.Pending)
        {
            throw new DomainException("Đơn khiếu nại đã được xử lý (Không thể thay đổi quyết định).");
        }

        if (decision == ComplaintStatus.Pending)
        {
            throw new DomainException("Quyết định không hợp lệ (Chỉ chấp nhận Accepted hoặc Rejected).");
        }

        if (string.IsNullOrWhiteSpace(responseContent))
        {
            throw new DomainException("Nội dung phản hồi không được để trống.");
        }

        ResponseContent = responseContent.Trim();
        Status = decision;
        RepliedByUserId = organizerUserId;
        RepliedAtUtc = DateTime.UtcNow;
    }

    public IReadOnlyList<string> GetEvidenceLinks()
    {
        try
        {
            return JsonSerializer.Deserialize<List<string>>(EvidenceLinksJson) ?? new List<string>();
        }
        catch
        {
            return new List<string>();
        }
    }
}