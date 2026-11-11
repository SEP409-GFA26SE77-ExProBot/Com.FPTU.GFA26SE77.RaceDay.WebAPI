namespace RaceDay.Modules.Event.Contracts.Events;

public sealed record EventStaffAssignment(Guid MemberId, Guid EventId, string EventName, string? StationId);

/// <summary>Only active RaceStaff assignments linked to the authenticated account.</summary>
public interface IEventStaffAssignmentQuery
{
    Task<IReadOnlyList<EventStaffAssignment>> ListAsync(Guid eventId, Guid userId, CancellationToken cancellationToken = default);
}
