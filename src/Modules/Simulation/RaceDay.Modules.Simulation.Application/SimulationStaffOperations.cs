using System.Security.Cryptography;
using System.Text.Json;
using RaceDay.Modules.Event.Contracts.Simulation;
using RaceDay.Modules.Simulation.Contracts;
using RaceDay.Modules.Simulation.Domain;
using RaceDay.Shared.Domain.Exceptions;

namespace RaceDay.Modules.Simulation.Application;

public sealed partial class SimulationSessionBook
{
    public SimulationSnapshotDto? ActiveSnapshot(Guid eventId, Guid distanceId)
    {
        lock (_slots)
            return _bySlot.TryGetValue((eventId, distanceId), out var session) ? SnapshotOf(session.RunId) : null;
    }

    public async Task<SimulationSnapshotDto> RecordDecisionAsync(Guid runId, Guid registrationId,
        string status, string reason, string? evidence, Guid actorId, string checkpointId,
        bool organizer = false, CancellationToken cancellationToken = default,
        Guid? operationId = null, string? actorName = null, Func<CancellationToken, Task>? validateAccess = null)
    {
        var session = Find(runId);
        await session.CommitLock.WaitAsync(cancellationToken);
        try
        {
            EnsureCurrent(session, runId);
            if (validateAccess is not null) await validateAccess(cancellationToken);
            ValidateReason(reason);
            if (actorId == Guid.Empty || operationId == Guid.Empty || (evidence?.Length ?? 0) > 2000)
                throw new DomainException("Người thao tác, mã thao tác hoặc bằng chứng không hợp lệ.");
            var target = status?.Trim().ToUpperInvariant();
            if (target is not ("DNF" or "DNS" or "DQ")) throw new DomainException("Trạng thái phải là DNF, DNS hoặc DQ.");
            reason = reason.Trim(); evidence = evidence?.Trim();
            var hash = Hash(new { runId, registrationId, target, reason, evidence, actorId, checkpointId, organizer });
            var previous = operationId is Guid op
                ? await _journal.FindOperationAsync(op, cancellationToken) : null;
            if (previous is not null)
            {
                if (previous.RequestHash != hash) throw new DomainException("Mã thao tác đã được dùng với nội dung khác.");
                if (session.Decisions.Any(d => d.Id == previous.DecisionId)) return SnapshotOf(runId)!;
            }
            await EnsureCheckpointOpenAsync(session, checkpointId, cancellationToken);
            SimulationDecisionDto decision;
            lock (session.Gate)
            {
                EnsureDecisionsEditable(session);
                var view = session.Engine.Capture();
                var athlete = view.Athletes.SingleOrDefault(x => x.RegistrationId == registrationId)
                    ?? throw new DomainException("VĐV không thuộc lượt giả lập.");
                var checkpoint = session.Checkpoints.SingleOrDefault(x => x.Id == checkpointId)
                    ?? throw new DomainException("Checkpoint không thuộc lượt giả lập.");
                if (session.Decisions.Any(x => x.RegistrationId == registrationId && x.Resolution == "Pending"))
                    throw new DomainException("VĐV đang có báo cáo chờ duyệt.");
                if (athlete.Motion is AthleteMotion.Dnf or AthleteMotion.Dns or AthleteMotion.Dq)
                    throw new DomainException("VĐV đã có quyết định DNF/DNS/DQ.");
                if (!organizer && target == "DNS" && !checkpoint.Type.Equals("start", StringComparison.OrdinalIgnoreCase))
                    throw new DomainException("Chỉ staff tại Start được ghi DNS.");
                var scope = CheckpointScope(checkpoint, session.Checkpoints);
                if (!organizer && target == "DNF" && !scope.Contains(athlete.CourseKm, view.Phase == SimulationPhase.Ready))
                    throw new DomainException("VĐV không còn ở chặng thuộc checkpoint được giao; không thể ghi DNF.");
                if (!organizer && target == "DQ" && !scope.CanReport(athlete.CourseKm, view.Phase == SimulationPhase.Ready,
                        athlete.Splits.Any(s => s.CheckpointId == checkpoint.Id)))
                    throw new DomainException("VĐV chưa thuộc chặng và chưa có ghi nhận qua checkpoint được giao.");
                if (target != "DQ" || organizer) session.Engine.ValidateRaceStatus(registrationId, Motion(target));
                decision = new(previous?.DecisionId ?? Guid.NewGuid(), registrationId, athlete.Bib,
                    athlete.Motion.ToString(), target, reason, evidence, actorId, checkpointId,
                    previous?.OccurredAtUtc ?? DateTime.UtcNow, target == "DQ" && !organizer ? "Pending" : "Approved",
                    CreatedAtSimSeconds: previous?.SimSeconds ?? view.SimulatedSeconds,
                    ActorName: previous?.ActorName ?? actorName);
            }
            if (previous is null)
            {
                var athlete = session.Athletes.Single(a => a.RegistrationId == registrationId);
                await _journal.AppendAsync(new(Guid.NewGuid(), 0, runId, session.EventId, session.DistanceId, checkpointId,
                    decision.Resolution == "Pending" ? "DQ_REPORTED" : target + "_RECORDED", actorId,
                    actorName ?? actorId.ToString(), organizer ? "Organizer" : "Race-staff", registrationId,
                    athlete.Bib, athlete.FullName, decision.CreatedAtUtc, decision.CreatedAtSimSeconds,
                    decision.PreviousStatus, decision.Resolution == "Pending" ? decision.PreviousStatus : target,
                    reason, evidence, decision.Id, operationId ?? Guid.NewGuid(), hash), cancellationToken);
            }
            SimulationSnapshotDto snapshot;
            lock (session.Gate)
            {
                if (target != "DQ" || organizer) session.Engine.SetRaceStatus(registrationId, Motion(target));
                session.Decisions.Add(decision);
                session.Version++;
                snapshot = Build(session);
            }
            await _broadcaster.PublishAsync(snapshot, cancellationToken);
            return snapshot;
        }
        finally { session.CommitLock.Release(); }
    }

    public async Task<SimulationSnapshotDto> ReviewDecisionAsync(Guid runId, Guid decisionId, bool approve,
        string reason, Guid actorId, CancellationToken cancellationToken = default,
        Guid? operationId = null, string? actorName = null)
    {
        var session = Find(runId);
        await session.CommitLock.WaitAsync(cancellationToken);
        try
        {
            EnsureCurrent(session, runId);
            ValidateReason(reason);
            if (actorId == Guid.Empty || operationId == Guid.Empty) throw new DomainException("Người thao tác hoặc mã thao tác không hợp lệ.");
            reason = reason.Trim();
            var hash = Hash(new { runId, decisionId, approve, reason, actorId });
            var previous = operationId is Guid op
                ? await _journal.FindOperationAsync(op, cancellationToken) : null;
            if (previous is not null && previous.RequestHash != hash)
                throw new DomainException("Mã thao tác đã được dùng với nội dung khác.");
            var decision = session.Decisions.SingleOrDefault(d => d.Id == decisionId)
                ?? throw new DomainException("Không tìm thấy báo cáo.");
            if (previous is not null && decision.Resolution != "Pending") return SnapshotOf(runId)!;
            await EnsureCheckpointOpenAsync(session, decision.CheckpointId, cancellationToken);
            SimulationDecisionDto reviewed;
            string before;
            lock (session.Gate)
            {
                EnsureDecisionsEditable(session);
                if (decision.Resolution != "Pending") throw new DomainException("Báo cáo không còn chờ duyệt.");
                before = session.Engine.Capture().Athletes.Single(a => a.RegistrationId == decision.RegistrationId).Motion.ToString();
                if (approve) session.Engine.ValidateRaceStatus(decision.RegistrationId, AthleteMotion.Dq);
                reviewed = decision with { Resolution = approve ? "Approved" : "Rejected", ReviewedBy = actorId,
                    ReviewedAtUtc = previous?.OccurredAtUtc ?? DateTime.UtcNow, ReviewReason = reason,
                    ReviewedAtSimSeconds = previous?.SimSeconds ?? session.Engine.SimulatedSeconds,
                    ReviewedByName = previous?.ActorName ?? actorName };
            }
            if (previous is null)
            {
                var athlete = session.Athletes.Single(a => a.RegistrationId == decision.RegistrationId);
                await _journal.AppendAsync(new(Guid.NewGuid(), 0, runId, session.EventId, session.DistanceId, decision.CheckpointId,
                    approve ? "DQ_APPROVED" : "DQ_REJECTED", actorId, actorName ?? actorId.ToString(), "Organizer",
                    athlete.RegistrationId, athlete.Bib, athlete.FullName, reviewed.ReviewedAtUtc!.Value,
                    reviewed.ReviewedAtSimSeconds, before, approve ? "DQ" : before, reason, decision.Evidence,
                    decisionId, operationId ?? Guid.NewGuid(), hash), cancellationToken);
            }
            SimulationSnapshotDto snapshot;
            lock (session.Gate)
            {
                if (approve) session.Engine.SetRaceStatus(decision.RegistrationId, AthleteMotion.Dq);
                session.Decisions[session.Decisions.FindIndex(d => d.Id == decisionId)] = reviewed;
                session.Version++;
                snapshot = Build(session);
            }
            await _broadcaster.PublishAsync(snapshot, cancellationToken);
            return snapshot;
        }
        finally { session.CommitLock.Release(); }
    }

    private async Task EnsureCheckpointOpenAsync(Session session, string checkpointId, CancellationToken ct)
    {
        if (await _journal.GetExportAsync(session.RunId, checkpointId, ct) is not null)
            throw new DomainException("Checkpoint đã gửi báo cáo và được khóa trong lượt này.");
    }

    private static string Hash(object request) => Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(request)));
    private static AthleteMotion Motion(string status) => status switch { "DNS" => AthleteMotion.Dns, "DNF" => AthleteMotion.Dnf, _ => AthleteMotion.Dq };

    private static void ValidateReason(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length > 1000)
            throw new DomainException("Lý do bắt buộc, tối đa 1000 ký tự.");
    }

    private static void EnsureDecisionsEditable(Session session)
    {
        if (session.CommitState == "Committing" || session.Committed.Count > 0 || session.Unresolved.Count > 0)
            throw new DomainException("Lượt đang ghi hoặc đã ghi kết quả; không được thay đổi quyết định.");
    }

    private static StaffCheckpointScope CheckpointScope(SimulationCourseCheckpoint checkpoint,
        IReadOnlyList<SimulationCourseCheckpoint> checkpoints)
    {
        var index = checkpoints.ToList().FindIndex(x => x.Id == checkpoint.Id);
        var isStart = checkpoint.Type.Equals("start", StringComparison.OrdinalIgnoreCase);
        return new(isStart ? checkpoint.Km : checkpoints[index - 1].Km, checkpoint.Km, isStart);
    }
}
