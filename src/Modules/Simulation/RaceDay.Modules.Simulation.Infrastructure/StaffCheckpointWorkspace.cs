using RaceDay.Modules.Event.Contracts.Events;
using RaceDay.Modules.Map.Contracts.Courses;
using RaceDay.Modules.Simulation.Application;
using RaceDay.Modules.Simulation.Contracts;
using RaceDay.Shared.Domain.Exceptions;

namespace RaceDay.Modules.Simulation.Infrastructure;

public sealed class StaffCheckpointWorkspace(
    IEventStaffAssignmentQuery assignments, IRaceCourseReadQuery courses, SimulationSessionBook book)
    : IStaffCheckpointWorkspace
{
    public async Task<StaffCheckpointSnapshotDto> ReadAsync(Guid eventId, Guid userId, CancellationToken cancellationToken = default)
    {
        var members = await assignments.ListAsync(eventId, userId, cancellationToken);
        if (members.Count != 1)
            throw new DomainException("Không có phân công Race-staff Active duy nhất cho tài khoản tại giải này.");
        var member = members[0];
        var station = member.StationId?.Split(':', 3);
        if (station is not { Length: 3 } || station[0] != "map" || !Guid.TryParse(station[1], out var distanceId))
            throw new DomainException("Bạn chưa được phân công checkpoint trên bản đồ. Liên hệ organizer để được gán mốc.");
        var course = await courses.GetAsync(eventId, distanceId, cancellationToken)
            ?? throw new DomainException("Cự ly được giao không có cung đường.");
        var checkpoint = course.Checkpoints.SingleOrDefault(x => x.Id == station[2])
            ?? throw new DomainException("Checkpoint được giao đã bị xóa. Liên hệ organizer để phân công lại.");
        return StaffCheckpointProjection.Build(eventId, member.EventName, distanceId, course.Title,
            course.DistanceKm, course.Polyline.Select(x => new SimulationPointDto(x.Lat, x.Lng)).ToArray(),
            new SimulationCheckpointDto(checkpoint.Id, checkpoint.Name, checkpoint.Km, checkpoint.Lat, checkpoint.Lng, checkpoint.Type),
            book.ActiveSnapshot(eventId, distanceId));
    }
}
