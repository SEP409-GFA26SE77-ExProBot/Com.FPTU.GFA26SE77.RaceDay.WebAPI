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

    public Task<SimulationSnapshotDto> RecordDecisionAsync(Guid runId, Guid registrationId,
        string status, string reason, string? evidence, Guid actorId, string checkpointId,
        bool organizer = false, CancellationToken cancellationToken = default)
        => MutateAsync(runId, session =>
        {
            EnsureDecisionsEditable(session);
            ValidateReason(reason);
            if (actorId == Guid.Empty || (evidence?.Length ?? 0) > 2000)
                throw new DomainException("Người thao tác hoặc bằng chứng không hợp lệ (tối đa 2000 ký tự).");
            var view = session.Engine.Capture();
            var athlete = view.Athletes.SingleOrDefault(x => x.RegistrationId == registrationId)
                ?? throw new DomainException("VĐV không thuộc lượt giả lập.");
            var checkpoint = session.Checkpoints.SingleOrDefault(x => x.Id == checkpointId)
                ?? throw new DomainException("Checkpoint không thuộc lượt giả lập.");
            var target = status?.Trim().ToUpperInvariant();
            if (target is not ("DNF" or "DNS" or "DQ"))
                throw new DomainException("Trạng thái phải là DNF, DNS hoặc DQ.");
            if (session.Decisions.Any(x => x.RegistrationId == registrationId && x.Resolution == "Pending"))
                throw new DomainException("VĐV đang có báo cáo chờ duyệt.");
            if (athlete.Motion is AthleteMotion.Dnf or AthleteMotion.Dns or AthleteMotion.Dq)
                throw new DomainException("VĐV đã có quyết định DNF/DNS/DQ.");
            if (!organizer && target == "DNS" && !checkpoint.Type.Equals("start", StringComparison.OrdinalIgnoreCase))
                throw new DomainException("Chỉ staff tại Start được ghi DNS.");
            if (!organizer && target != "DNS" && !IsAtCheckpoint(view, athlete, checkpoint, session.Checkpoints))
                throw new DomainException("VĐV chưa ở chặng thuộc checkpoint được giao.");
            if (target != "DQ" || organizer)
                session.Engine.SetRaceStatus(registrationId, target switch
                {
                    "DNS" => AthleteMotion.Dns, "DNF" => AthleteMotion.Dnf, _ => AthleteMotion.Dq
                });
            session.Decisions.Add(new SimulationDecisionDto(Guid.NewGuid(), registrationId, athlete.Bib,
                athlete.Motion.ToString(), target, reason.Trim(), evidence?.Trim(), actorId, checkpointId,
                DateTime.UtcNow, target == "DQ" && !organizer ? "Pending" : "Approved",
                CreatedAtSimSeconds: view.SimulatedSeconds));
        }, cancellationToken);

    public Task<SimulationSnapshotDto> ReviewDecisionAsync(Guid runId, Guid decisionId, bool approve,
        string reason, Guid actorId, CancellationToken cancellationToken = default)
        => MutateAsync(runId, session =>
        {
            EnsureDecisionsEditable(session);
            ValidateReason(reason);
            var index = session.Decisions.FindIndex(x => x.Id == decisionId);
            if (index < 0 || session.Decisions[index].Resolution != "Pending")
                throw new DomainException("Báo cáo không còn chờ duyệt.");
            var decision = session.Decisions[index];
            if (approve) session.Engine.SetRaceStatus(decision.RegistrationId, AthleteMotion.Dq);
            session.Decisions[index] = decision with
            {
                Resolution = approve ? "Approved" : "Rejected", ReviewedBy = actorId,
                ReviewedAtUtc = DateTime.UtcNow, ReviewReason = reason.Trim(),
                ReviewedAtSimSeconds = session.Engine.Capture().SimulatedSeconds
            };
        }, cancellationToken);

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

    // The checkpoint staff covers the segment after the previous checkpoint and before the next.
    public static bool IsAtCheckpoint(SimulationEngineView view, SimulationAthleteView athlete,
        SimulationCourseCheckpoint checkpoint, IReadOnlyList<SimulationCourseCheckpoint> checkpoints)
    {
        if (view.Phase == SimulationPhase.Ready) return checkpoint.Type.Equals("start", StringComparison.OrdinalIgnoreCase);
        var index = checkpoints.ToList().FindIndex(x => x.Id == checkpoint.Id);
        var lower = index > 0 ? checkpoints[index - 1].Km : 0;
        var upper = index + 1 < checkpoints.Count ? checkpoints[index + 1].Km : double.PositiveInfinity;
        return athlete.CourseKm >= lower && athlete.CourseKm < upper;
    }
}
