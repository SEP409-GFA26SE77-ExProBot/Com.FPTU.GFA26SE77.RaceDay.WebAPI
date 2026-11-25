using RaceDay.Modules.Event.Contracts.Simulation;
using RaceDay.Modules.Simulation.Contracts;
using RaceDay.Modules.Simulation.Domain;
using RaceDay.Shared.Domain.Exceptions;

namespace RaceDay.Modules.Simulation.Application;

public sealed partial class SimulationSessionBook
{
    public async Task<CheckpointResultExport> SubmitCheckpointExportAsync(Guid runId, string checkpointId,
        Guid actorId, string actorName, CancellationToken ct = default, Func<CancellationToken, Task>? validateAccess = null)
    {
        var session = Find(runId);
        await session.CommitLock.WaitAsync(ct);
        try
        {
            EnsureCurrent(session, runId);
            if (validateAccess is not null) await validateAccess(ct);
            var store = _journal;
            var existing = await store.GetExportAsync(runId, checkpointId, ct);
            if (existing is not null) return existing;
            var export = BuildCheckpointExport(runId, checkpointId, actorId) with { StaffName = actorName };
            var entry = new SimulationJournalEntry(Guid.NewGuid(), 0, runId, session.EventId, session.DistanceId,
                checkpointId, "CHECKPOINT_SUBMITTED", actorId, actorName, "Race-staff", null, null, null,
                export.SubmittedAtUtc, session.Engine.SimulatedSeconds, null, null, null, null, null,
                Guid.NewGuid(), "");
            var result = await store.SubmitExportAsync(export, entry, ct);
            SimulationSnapshotDto snapshot;
            lock (session.Gate) { session.Version++; snapshot = Build(session); }
            await _broadcaster.PublishAsync(snapshot, ct);
            return result;
        }
        finally { session.CommitLock.Release(); }
    }

    public CheckpointResultExport BuildCheckpointExport(Guid runId, string checkpointId, Guid staffUserId)
    {
        var session = Find(runId);
        lock (session.Gate)
        {
            if (session.RunId != runId) throw new DomainException("Lượt đã thay đổi. Tải lại checkpoint.");
            var view = session.Engine.Capture();
            if (view.Phase != SimulationPhase.Finished)
                throw new DomainException("Chỉ gửi kết quả chặng khi lượt giả lập đã kết thúc.");
            if (session.Decisions.Any(d => d.CheckpointId == checkpointId && d.Resolution == "Pending"))
                throw new DomainException("Cần xử lý hết báo cáo DQ tại checkpoint trước khi gửi kết quả.");
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
