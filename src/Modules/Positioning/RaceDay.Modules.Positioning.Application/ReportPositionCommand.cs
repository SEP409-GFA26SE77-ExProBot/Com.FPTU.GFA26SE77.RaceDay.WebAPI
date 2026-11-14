using MediatR;
using RaceDay.Modules.Event.Contracts.Events;
using RaceDay.Modules.Map.Contracts.Courses;
using RaceDay.Modules.Positioning.Contracts;
using RaceDay.Modules.Positioning.Domain;
using RaceDay.Shared.Domain.Exceptions;

namespace RaceDay.Modules.Positioning.Application;

public sealed record PositionSample(GeoPoint Point, double AccuracyMeters, DateTimeOffset CapturedAtUtc);

public interface IPositionTracker
{
    Task<bool> RecordAsync(Guid eventId, Guid userId, PositionSample sample, bool? isOutside, CancellationToken ct);
}

public interface IPositionAlertPublisher
{
    void Publish(PositionAlert alert);
}

public sealed record ReportPositionCommand(Guid EventId, Guid UserId, ReportPositionBody Body) : IRequest<PositionReceipt?>;

public sealed class ReportPositionCommandHandler(
    PositioningAccess access,
    IEventCoursesReadQuery courses,
    IPositionTracker tracker,
    IPositionAlertPublisher alerts,
    TimeProvider clock) : IRequestHandler<ReportPositionCommand, PositionReceipt?>
{
    public async Task<PositionReceipt?> Handle(ReportPositionCommand request, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var body = request.Body;
        if (body is not { Latitude: double lat, Longitude: double lng, AccuracyMeters: double accuracy, CapturedAtUtc: { } capturedAt }
            || !new GeoPoint(lat, lng).IsValid || !double.IsFinite(accuracy) || accuracy < 0
            || capturedAt < now.AddMinutes(-2) || capturedAt > now.AddSeconds(15))
        {
            throw new DomainException("Tọa độ, độ chính xác hoặc thời điểm GPS không hợp lệ.");
        }

        var assignments = await access.GetReportingAssignmentsAsync(request.EventId, request.UserId, cancellationToken);
        if (assignments is null) return null;

        var point = new GeoPoint(lat, lng);
        var nearestEdge = double.PositiveInfinity;
        PositioningStaffAssignment? nearestAssignment = null;
        if (assignments.Any(assignment => assignment.StationIds.Any(HasGeometry)))
        {
            var routes = await courses.ListAsync(request.EventId, cancellationToken);
            foreach (var assignment in assignments)
            {
                foreach (var stationId in assignment.StationIds)
                {
                    var distance = DistanceFromZone(point, stationId, routes);
                    if (distance >= nearestEdge) continue;
                    nearestEdge = distance;
                    nearestAssignment = assignment;
                }
            }
        }

        bool? outside = nearestAssignment is null || accuracy > 0 && Math.Abs(nearestEdge) <= accuracy
            ? null
            : nearestEdge > 0;

        var becameOutside = await tracker.RecordAsync(request.EventId, request.UserId,
            new PositionSample(point, accuracy, capturedAt), outside, cancellationToken);
        if (becameOutside && nearestAssignment is not null)
        {
            alerts.Publish(new PositionAlert(request.EventId, nearestAssignment.Id, nearestAssignment.Name, nearestAssignment.Role, now));
        }

        return new PositionReceipt(nearestAssignment is null ? "unconfigured" : outside switch
        {
            true => "outside",
            false => "inside",
            null => "uncertain"
        });
    }

    private static bool HasGeometry(string stationId) => stationId == "duty:float" || stationId.StartsWith("map:", StringComparison.Ordinal);

    private static double DistanceFromZone(GeoPoint position, string stationId, IReadOnlyList<RaceCourseSnapshot> routes)
    {
        if (stationId == "duty:float")
        {
            return routes.Select(route => ActivityArea.DistanceToCourse(position,
                    route.Polyline.Select(point => new GeoPoint(point.Lat, point.Lng)).ToArray()))
                .DefaultIfEmpty(double.PositiveInfinity).Min() - ActivityArea.CourseRadiusMeters;
        }

        var parts = stationId.Split(':', 3);
        if (parts.Length != 3 || parts[0] != "map" || !Guid.TryParse(parts[1], out var distanceId))
        {
            return double.PositiveInfinity;
        }

        var checkpoint = routes.FirstOrDefault(route => route.DistanceId == distanceId)?
            .Checkpoints.FirstOrDefault(checkpoint => checkpoint.Id == parts[2]);
        if (checkpoint is null) return double.PositiveInfinity;
        var center = new GeoPoint(checkpoint.Lat, checkpoint.Lng);
        return center.IsValid
            ? ActivityArea.DistanceToCheckpoint(position, center) - ActivityArea.CheckpointRadiusMeters
            : double.PositiveInfinity;
    }
}
