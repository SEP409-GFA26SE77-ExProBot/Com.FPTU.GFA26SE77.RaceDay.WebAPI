using MediatR;
using RaceDay.Modules.Event.Contracts.Events;
using RaceDay.Modules.Positioning.Contracts;

namespace RaceDay.Modules.Positioning.Application;

public sealed record ListLiveStaffPositionsQuery(Guid EventId, Guid UserId, int? Offset) : IRequest<LiveStaffPositionPage?>;

public sealed class ListLiveStaffPositionsQueryHandler(
    PositioningAccess access,
    IEventPositioningAccessQuery staff,
    IPositionTracker tracker,
    TimeProvider clock) : IRequestHandler<ListLiveStaffPositionsQuery, LiveStaffPositionPage?>
{
    private const int PageSize = 100;
    internal static readonly TimeSpan LiveSampleAge = TimeSpan.FromSeconds(20);

    public async Task<LiveStaffPositionPage?> Handle(ListLiveStaffPositionsQuery request, CancellationToken cancellationToken)
    {
        if (!await access.CanWatchAsync(request.EventId, request.UserId, cancellationToken)) return null;

        var assignments = await staff.ListActiveAsync(request.EventId, null, cancellationToken);
        var people = assignments.GroupBy(assignment => assignment.UserId).OrderBy(group => group.Key);
        var window = request.Offset is { } offset ? people.Skip(offset).Take(PageSize + 1).ToArray() : people.ToArray();
        var cutoff = clock.GetUtcNow() - LiveSampleAge;
        var positions = new List<LiveStaffPosition>();
        foreach (var batch in (request.Offset.HasValue ? window.Take(PageSize) : window).Chunk(PageSize))
        {
            var samples = await Task.WhenAll(batch.Select(async person =>
            {
                var sample = await tracker.GetLatestAsync(request.EventId, person.Key, cancellationToken);
                return sample is null || sample.CapturedAtUtc < cutoff ? null : ToPosition(person.Key, person, sample);
            }));
            positions.AddRange(samples.OfType<LiveStaffPosition>());
        }

        return new LiveStaffPositionPage(positions, window.Length > PageSize ? request.Offset + PageSize : null);
    }

    internal static LiveStaffPosition ToPosition(Guid userId, IEnumerable<PositioningStaffAssignment> assignments, PositionSample sample)
    {
        var roles = assignments.Select(assignment => assignment.Role).Distinct().Order().ToArray();
        var stations = assignments.SelectMany(assignment => assignment.Stations)
            .GroupBy(station => station.Id)
            .Select(station => new LiveStaffStation(station.Key, station.Select(item => item.Label).FirstOrDefault(label => !string.IsNullOrWhiteSpace(label))))
            .ToArray();
        return new LiveStaffPosition(userId, assignments.First().Name, roles, stations,
            sample.Point.Latitude, sample.Point.Longitude, sample.CapturedAtUtc);
    }
}
