using RaceDay.Modules.Simulation.Contracts;
using RaceDay.Shared.Domain.Exceptions;

namespace RaceDay.Modules.Simulation.Application;

public interface IStaffCheckpointWorkspace
{
    Task<StaffCheckpointSnapshotDto> ReadAsync(Guid eventId, Guid userId, CancellationToken cancellationToken = default);
}

public static class StaffCheckpointProjection
{
    public static StaffCheckpointSnapshotDto Build(Guid eventId, string eventName, Guid distanceId,
        string title, double distanceKm, IReadOnlyList<SimulationPointDto> polyline,
        SimulationCheckpointDto checkpoint, SimulationSnapshotDto? run)
    {
        if (run is null)
            return new(eventId, eventName, distanceId, title, checkpoint, polyline, distanceKm,
                null, 0, 0, null, 0, 1, null, [], []);
        checkpoint = run.Checkpoints.SingleOrDefault(x => x.Id == checkpoint.Id)
            ?? throw new DomainException("Checkpoint đã thay đổi; cần organizer tạo lượt mới theo bản đồ hiện tại.");
        var ordered = run.Checkpoints.ToList();
        var index = ordered.FindIndex(x => x.Id == checkpoint.Id);
        var lower = index > 0 ? ordered[index - 1].Km : 0;
        var upper = index + 1 < ordered.Count ? ordered[index + 1].Km : double.PositiveInfinity;
        var editable = run.CommitState != "Committing" && run.CommittedRegistrationIds.Count == 0;
        var decisions = run.Decisions ?? [];
        var athletes = run.Athletes.Select(a =>
        {
            var stopped = a.Motion is "Dnf" or "Dns" or "Dq";
            var local = run.Phase == "Ready" ? checkpoint.Type == "start" : a.CourseKm >= lower && a.CourseKm < upper;
            var canAct = editable && !stopped && !decisions.Any(d => d.RegistrationId == a.RegistrationId && d.Resolution == "Pending");
            return new StaffCheckpointAthleteDto(a.RegistrationId, a.Bib, a.FullName, a.Motion, a.Sos,
                a.CourseKm, a.SpeedKmh, a.Splits.FirstOrDefault(s => s.CheckpointId == checkpoint.Id)?.SimSeconds,
                Math.Max(0, checkpoint.Km - a.CourseKm),
                canAct && local && (run.Phase is "Running" or "Paused") && a.Motion == "Racing",
                canAct && checkpoint.Type == "start" && run.Phase == "Ready",
                canAct && local);
        }).ToArray();
        return new(eventId, eventName, distanceId, run.Title, checkpoint, run.Polyline, run.DistanceKm,
            run.RunId, run.Generation, run.Version, run.Phase, run.SimulatedSeconds, run.PlaybackRate, run.CommitState,
            athletes, decisions.Where(d => d.CheckpointId == checkpoint.Id).ToArray());
    }
}
