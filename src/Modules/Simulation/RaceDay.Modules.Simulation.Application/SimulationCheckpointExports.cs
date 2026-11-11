using RaceDay.Modules.Event.Contracts.Simulation;
using RaceDay.Modules.Simulation.Domain;
using RaceDay.Shared.Domain.Exceptions;

namespace RaceDay.Modules.Simulation.Application;

public sealed partial class SimulationSessionBook
{
    public CheckpointResultExport BuildCheckpointExport(Guid runId, string checkpointId, Guid staffUserId)
    {
        var session = Find(runId);
        lock (session.Gate)
        {
            if (session.RunId != runId) throw new DomainException("Lượt đã thay đổi. Tải lại checkpoint.");
            var view = session.Engine.Capture();
            if (view.Phase != SimulationPhase.Finished)
                throw new DomainException("Chỉ gửi kết quả chặng khi lượt giả lập đã kết thúc.");
            var checkpoint = session.Checkpoints.SingleOrDefault(x => x.Id == checkpointId)
                ?? throw new DomainException("Checkpoint không thuộc lượt giả lập.");
            var rows = view.Athletes.Select(a => new CheckpointResultRow(
                a.RegistrationId, a.Bib, a.FullName, a.Motion.ToString(),
                a.Splits.FirstOrDefault(s => s.CheckpointId == checkpointId) is { } split
                    ? SimulationClockMath.MicrosToSeconds(split.SimMicros) : null)).ToArray();
            var decisions = session.Decisions.Where(d => d.CheckpointId == checkpointId)
                .Select(d => new CheckpointResultDecision(d.RegistrationId, d.Bib, d.Status, d.Reason, d.Evidence, d.Resolution))
                .ToArray();
            return new(runId, session.EventId, session.DistanceId, checkpoint.Id, checkpoint.Name,
                checkpoint.Km, staffUserId, DateTime.UtcNow, rows, decisions);
        }
    }
}
