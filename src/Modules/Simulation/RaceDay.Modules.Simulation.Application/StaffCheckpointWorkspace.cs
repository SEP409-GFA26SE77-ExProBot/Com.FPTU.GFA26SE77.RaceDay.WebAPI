using RaceDay.Modules.Simulation.Contracts;
using RaceDay.Shared.Domain.Exceptions;

namespace RaceDay.Modules.Simulation.Application;

public sealed class StaffCheckpointAccessException(string message) : DomainException("CHECKPOINT_ACCESS_DENIED: " + message);

public interface IStaffCheckpointWorkspace
{
    Task<StaffCheckpointSnapshotDto> ReadAsync(Guid eventId, Guid userId, CancellationToken cancellationToken = default);
}

public static class StaffCheckpointProjection
{
    public static StaffCheckpointSnapshotDto Build(Guid eventId, string eventName, Guid distanceId,
        string title, double distanceKm, IReadOnlyList<SimulationPointDto> polyline,
        SimulationCheckpointDto checkpoint, SimulationSnapshotDto? run, bool submitted = false)
    {
        if (run is null)
            return new(eventId, eventName, distanceId, title, checkpoint, polyline, distanceKm,
                null, 0, 0, null, 0, 1, null, [], []);
        checkpoint = run.Checkpoints.SingleOrDefault(x => x.Id == checkpoint.Id)
            ?? throw new DomainException("Checkpoint đã thay đổi; cần organizer tạo lượt mới theo bản đồ hiện tại.");
        var ordered = run.Checkpoints.ToList();
        var index = ordered.FindIndex(x => x.Id == checkpoint.Id);
        var isStart = checkpoint.Type.Equals("start", StringComparison.OrdinalIgnoreCase);
        var scope = new StaffCheckpointScope(isStart ? checkpoint.Km : ordered[index - 1].Km, checkpoint.Km, isStart);
        var editable = !submitted && run.DecisionsEditable && run.CommitState != "Committing" && run.CommittedRegistrationIds.Count == 0;
        var decisions = run.Decisions ?? [];
        var athletes = run.Athletes.Select(a =>
        {
            var stopped = a.Motion is "Dnf" or "Dns" or "Dq";
            var ready = run.Phase == "Ready";
            var local = scope.Contains(a.CourseKm, ready);
            var passage = a.Splits.FirstOrDefault(s => s.CheckpointId == checkpoint.Id);
            var canAct = editable && !stopped && !decisions.Any(d => d.RegistrationId == a.RegistrationId && d.Resolution == "Pending");
            return new StaffCheckpointAthleteDto(a.RegistrationId, a.Bib, a.FullName, a.Motion, a.Sos,
                a.CourseKm, a.SpeedKmh, passage?.SimSeconds,
                Math.Max(0, checkpoint.Km - a.CourseKm),
                canAct && local && (run.Phase is "Running" or "Paused") && a.Motion == "Racing",
                canAct && isStart && ready,
                canAct && scope.CanReport(a.CourseKm, ready, passage is not null), local,
                decisions.FirstOrDefault(d => d.RegistrationId == a.RegistrationId && d.CheckpointId == checkpoint.Id && d.Status == "DQ" && d.Resolution == "Pending"),
                decisions.Any(d => d.RegistrationId == a.RegistrationId && d.Status == "DQ" && d.Resolution == "Pending"));
        }).ToArray();
        return new(eventId, eventName, distanceId, run.Title, checkpoint, run.Polyline, run.DistanceKm,
            run.RunId, run.Generation, run.Version, run.Phase, run.SimulatedSeconds, run.PlaybackRate, run.CommitState,
            athletes, decisions.Where(d => d.CheckpointId == checkpoint.Id).ToArray(), scope.FromKm, scope.ToKm);
    }
}
