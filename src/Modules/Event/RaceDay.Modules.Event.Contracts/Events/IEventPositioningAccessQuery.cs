namespace RaceDay.Modules.Event.Contracts.Events;

public sealed record PositioningStaffAssignment(Guid Id, string Name, string Role, IReadOnlyList<string> StationIds);

public interface IEventPositioningAccessQuery
{
    Task<IReadOnlyList<PositioningStaffAssignment>> ListActiveAsync(
        Guid eventId, Guid userId, CancellationToken cancellationToken = default);
}
