using MediatR;
using Microsoft.Extensions.Logging;
using RaceDay.Modules.Event.Contracts.Events;
using RaceDay.Modules.Map.Contracts.Courses;
using RaceDay.Modules.Positioning.Contracts;
using RaceDay.Modules.RaceMonitoring.Contracts.StaffIncidents;
using RaceDay.Modules.Positioning.Domain;
using RaceDay.Shared.Domain.Exceptions;

namespace RaceDay.Modules.Positioning.Application;

public sealed record PositionSample(GeoPoint Point, double AccuracyMeters, DateTimeOffset CapturedAtUtc);
/// <param name="BecameInside">Inside now while the last known state was outside or unknown (first sample, or the
/// state expired). An open out-of-zone row, if any, is closed then.</param>
public sealed record PositionRecordResult(bool Accepted, bool BecameOutside, bool BecameInside = false);

public interface IPositionTracker
{
    Task<PositionRecordResult> RecordAsync(Guid eventId, Guid userId, PositionSample sample, bool? isOutside, CancellationToken ct);
    Task<PositionSample?> GetLatestAsync(Guid eventId, Guid userId, CancellationToken ct);
}

public interface IPositionEventPublisher
{
    void Publish(PositionAlert alert);
    void Publish(Guid eventId, LiveStaffPosition position);
}

public sealed record ReportPositionCommand(Guid EventId, Guid UserId, ReportPositionBody Body) : IRequest<PositionReceipt?>;

public sealed class ReportPositionCommandHandler(
    PositioningAccess access,
    IEventCoursesReadQuery courses,
    IPositionTracker tracker,
    IPositionEventPublisher events,
    IStaffCheckInStore checkIns,
    IStaffIncidentSink incidents,
    ILogger<ReportPositionCommandHandler> logger,
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

        var reporting = await access.GetReportingAccessAsync(request.EventId, request.UserId, cancellationToken);
        if (reporting is null) return null;
        if (!reporting.IsOrganizer && await checkIns.GetAsync(request.EventId, request.UserId, cancellationToken) is null)
        {
            return PositionReceipt.NotCheckedIn;
        }

        var point = new GeoPoint(lat, lng);
        var nearestEdge = double.PositiveInfinity;
        PositioningStaffAssignment? nearestAssignment = null;
        var assignments = reporting.Assignments;
        if (assignments.Any(assignment => assignment.Stations.Any(station => HasGeometry(station.Id))))
        {
            var routes = await courses.ListAsync(request.EventId, cancellationToken);
            foreach (var assignment in assignments)
            {
                foreach (var station in assignment.Stations)
                {
                    var distance = DistanceFromZone(point, station.Id, routes);
                    if (distance >= nearestEdge) continue;
                    nearestEdge = distance;
                    nearestAssignment = assignment;
                }
            }
        }

        bool? outside = nearestAssignment is null || accuracy > 0 && Math.Abs(nearestEdge) <= accuracy
            ? null
            : nearestEdge > 0;

        var alertZone = reporting.AlertsStartAt is { } alertsStartAt && now >= alertsStartAt
            ? outside
            : null;
        var sample = new PositionSample(point, accuracy, capturedAt);
        var recorded = await tracker.RecordAsync(request.EventId, request.UserId, sample, alertZone, cancellationToken);
        if (recorded.Accepted && assignments.Count > 0 && capturedAt >= now - ListLiveStaffPositionsQueryHandler.LiveSampleAge)
        {
            events.Publish(request.EventId, ListLiveStaffPositionsQueryHandler.ToPosition(request.UserId, assignments, sample));
        }
        if (recorded.BecameOutside && nearestAssignment is not null)
        {
            events.Publish(new PositionAlert(request.EventId, request.UserId, nearestAssignment.Id, nearestAssignment.Name, nearestAssignment.Role, now));
            var staffMember = new StaffIncidentStaff(request.EventId, request.UserId, nearestAssignment.Name,
                nearestAssignment.Role, StaffZones.Describe([nearestAssignment]));
            await RecordIncidentAsync(ct => incidents.RecordLeftZoneAsync(
                staffMember, new StaffIncidentPoint(lat, lng, accuracy), capturedAt, ct), cancellationToken);
        }
        else if (recorded.BecameInside)
        {
            await RecordIncidentAsync(ct => incidents.RecordReturnedAsync(
                request.EventId, request.UserId, capturedAt, ct), cancellationToken);
        }

        return new PositionReceipt(nearestAssignment is null ? "unconfigured" : outside switch
        {
            true => "outside",
            false => "inside",
            null => "uncertain"
        });
    }

    /// <summary>The incident log is secondary: a database error must not make the GPS report fail.</summary>
    private async Task RecordIncidentAsync(Func<CancellationToken, Task> record, CancellationToken cancellationToken)
    {
        try
        {
            await record(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Could not record a staff zone change.");
        }
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
