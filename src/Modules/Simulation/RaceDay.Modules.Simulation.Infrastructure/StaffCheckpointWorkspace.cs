using RaceDay.Modules.Event.Contracts.Events;
using RaceDay.Modules.Event.Contracts.Simulation;
using RaceDay.Modules.Map.Contracts.Courses;
using RaceDay.Modules.Simulation.Application;
using RaceDay.Modules.Simulation.Contracts;

namespace RaceDay.Modules.Simulation.Infrastructure;

public sealed class StaffCheckpointWorkspace(
    IEventStaffAssignmentQuery assignments, IRaceCourseReadQuery courses, SimulationSessionBook book,
    ISimulationJournalStore journal)
    : IStaffCheckpointWorkspace
{
    public async Task<StaffCheckpointSnapshotDto> ReadAsync(Guid eventId, Guid userId, CancellationToken cancellationToken = default)
    {
        var members = await assignments.ListAsync(eventId, userId, cancellationToken);
        if (members.Count != 1)
            throw new StaffCheckpointAccessException("Không có phân công Race-staff Active duy nhất cho tài khoản tại giải này.");
        var member = members[0];
        var station = member.StationId?.Split(':', 3);
        if (station is not { Length: 3 } || station[0] != "map" || !Guid.TryParse(station[1], out var distanceId))
            throw new StaffCheckpointAccessException("Bạn chưa được phân công checkpoint trên bản đồ. Liên hệ organizer để được gán mốc.");
        var course = await courses.GetAsync(eventId, distanceId, cancellationToken)
            ?? throw new StaffCheckpointAccessException("Cự ly được giao không có cung đường.");
        var checkpoint = course.Checkpoints.SingleOrDefault(x => x.Id == station[2])
            ?? throw new StaffCheckpointAccessException("Checkpoint được giao đã bị xóa. Liên hệ organizer để phân công lại.");
        var run = book.ActiveSnapshot(eventId, distanceId);
        var export = run is not null ? await journal.GetExportAsync(run.RunId, checkpoint.Id, cancellationToken) : null;
        var result = StaffCheckpointProjection.Build(eventId, member.EventName, distanceId, course.Title,
            course.DistanceKm, course.Polyline.Select(x => new SimulationPointDto(x.Lat, x.Lng)).ToArray(),
            new SimulationCheckpointDto(checkpoint.Id, checkpoint.Name, checkpoint.Km, checkpoint.Lat, checkpoint.Lng, checkpoint.Type),
            run, export is not null);
        var blocked = export is not null ? "Báo cáo checkpoint đã được gửi và khóa."
            : run is null ? "Chờ organizer tạo lượt giả lập."
            : run.Phase != "Finished" ? "Chỉ gửi báo cáo khi lượt đã kết thúc."
            : result.Decisions.Any(d => d.Resolution == "Pending") ? "Cần xử lý hết báo cáo DQ tại checkpoint." : null;
        return result with {
            HistoryVersion = await journal.VersionAsync(eventId, distanceId, checkpoint.Id, cancellationToken),
            CanSubmitExport = blocked is null, ExportBlockedReason = blocked,
            SubmittedAtUtc = export?.SubmittedAtUtc, SubmittedBy = export?.StaffUserId, SubmittedByName = export?.StaffName
        };
    }
}
