using RaceDay.Shared.Domain.Abstractions;
using RaceDay.Shared.Domain.Exceptions;

namespace RaceDay.Modules.Event.Domain.Disputes;

public enum EventDisputeStatus
{
    Open = 0,
    Resolved = 1,
    Rejected = 2
}

public sealed class EventDispute : AggregateRoot
{
    public const int MaxTitleLength = 200;
    public const int MaxDescriptionLength = 4000;

    public Guid EventId { get; private set; }
    public Guid RunnerUserId { get; private set; }
    public string Title { get; private set; } = string.Empty;
    public string Description { get; private set; } = string.Empty;
    public EventDisputeStatus Status { get; private set; } = EventDisputeStatus.Open;
    public DateTime CreatedAtUtc { get; private set; }

    private EventDispute() { }

    public static EventDispute Create(
        Guid eventId, Guid runnerUserId, string title, string description, DateTime createdAtUtc)
    {
        if (eventId == Guid.Empty || runnerUserId == Guid.Empty)
            throw new DomainException("Giải chạy hoặc vận động viên không hợp lệ.");

        var trimmedTitle = (title ?? string.Empty).Trim();
        var trimmedDescription = (description ?? string.Empty).Trim();
        if (trimmedTitle.Length is 0 or > MaxTitleLength)
            throw new DomainException($"Tiêu đề khiếu nại phải có từ 1 đến {MaxTitleLength} ký tự.");
        if (trimmedDescription.Length is 0 or > MaxDescriptionLength)
            throw new DomainException($"Nội dung khiếu nại phải có từ 1 đến {MaxDescriptionLength} ký tự.");

        return new EventDispute
        {
            EventId = eventId,
            RunnerUserId = runnerUserId,
            Title = trimmedTitle,
            Description = trimmedDescription,
            CreatedAtUtc = createdAtUtc
        };
    }
}
