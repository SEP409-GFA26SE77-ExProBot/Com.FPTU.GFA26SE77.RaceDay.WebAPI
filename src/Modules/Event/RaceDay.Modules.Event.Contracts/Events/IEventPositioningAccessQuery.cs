namespace RaceDay.Modules.Event.Contracts.Events;

public sealed record PositioningStaffStation(string Id, string? Label);

public sealed record PositioningStaffAssignment(
    Guid Id, Guid UserId, string Name, string Role, IReadOnlyList<PositioningStaffStation> Stations);

public interface IEventPositioningAccessQuery
{
    Task<IReadOnlyList<PositioningStaffAssignment>> ListActiveAsync(
        Guid eventId, Guid? userId, CancellationToken cancellationToken = default);
}
