using RaceDay.Modules.Event.Domain.Disputes;

namespace RaceDay.Modules.Event.Application.Disputes;

public sealed record RunnerPageDto<T>(IReadOnlyList<T> Items, int TotalCount, int Page, int PageSize)
{
    public int TotalPages => (int)Math.Ceiling(TotalCount / (double)PageSize);
}

public sealed record RecentJoinedEventDto(
    Guid Id, string Name, DateOnly Date, DateOnly? EndDate, string Location);

public sealed record EventDisputeDto(
    Guid Id,
    Guid EventId,
    string EventName,
    string Title,
    string Description,
    string Status,
    DateTime CreatedAtUtc)
{
    public static EventDisputeDto From(EventDispute dispute, string eventName) => new(
        dispute.Id, dispute.EventId, eventName, dispute.Title, dispute.Description,
        dispute.Status.ToString().ToLowerInvariant(), dispute.CreatedAtUtc);
}
