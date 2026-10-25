using System;
using System.Collections.Generic;
using System.Text;
using RaceDay.Shared.Domain.Abstractions;
using RaceDay.Shared.Domain.Exceptions;
namespace RaceDay.Modules.Event.Domain.Complaints;

public enum ComplaintStatus
{
    Pending = 0,
    Replied = 1
}

public sealed class EventComplaint : AggregateRoot
{
    public Guid EventId { get; private set; }
    public Guid DistanceId { get; private set; }
    public Guid UserId { get; private set; }
    public string Content { get; private set; } = string.Empty;
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
        string content)
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

        if (string.IsNullOrWhiteSpace(content))
        {
            throw new DomainException("Nội dung khiếu nại không được để trống.");
        }

        return new EventComplaint
        {
            EventId = eventId,
            DistanceId = distanceId,
            UserId = userId,
            Content = content.Trim(),
            Status = ComplaintStatus.Pending,
            CreatedAtUtc = DateTime.UtcNow
        };
    }

    public void Reply(Guid organizerUserId, string responseContent)
    {
        if (organizerUserId == Guid.Empty)
        {
            throw new DomainException("Thông tin Ban tổ chức không hợp lệ.");
        }

        if (string.IsNullOrWhiteSpace(responseContent))
        {
            throw new DomainException("Nội dung phản hồi không được để trống.");
        }

        ResponseContent = responseContent.Trim();
        Status = ComplaintStatus.Replied;
        RepliedByUserId = organizerUserId;
        RepliedAtUtc = DateTime.UtcNow;
    }
}