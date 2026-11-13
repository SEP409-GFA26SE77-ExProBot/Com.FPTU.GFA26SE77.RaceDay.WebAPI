using RaceDay.Modules.Event.Domain.Events;
using Microsoft.EntityFrameworkCore;
using RaceDay.Modules.Event.Application.Simulation;
using RaceDay.Modules.Event.Contracts.Simulation;
using RaceDay.Modules.Event.Domain.Registrations;
using RaceDay.Modules.Event.Infrastructure.Persistence;
using RaceDay.Shared.Domain.Exceptions;

namespace RaceDay.Modules.Event.Infrastructure.Simulation;

public sealed class EfSimulationStatusWriter(EventDbContext db, ISimulationDemoGate gate) : ISimulationStatusWriter
{
    public async Task<SimulationCommitItemResult> WriteAsync(Guid eventId, Guid distanceId,
        SimulationFinishDraft draft, CancellationToken cancellationToken = default)
    {
        if (!gate.Enabled) throw new DomainException("Giả lập đang tắt.");
        var decision = draft.Decision ?? throw new DomainException("Thiếu quyết định.");
        if (!Enum.TryParse<RaceStatus>(decision.Status, out var target)
            || target is not (RaceStatus.DNF or RaceStatus.DNS or RaceStatus.DQ))
            throw new DomainException("Quyết định không hợp lệ.");
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var row = await db.Registrations.AsNoTracking().SingleOrDefaultAsync(x => x.Id == draft.RegistrationId, cancellationToken);
        if (row is null || row.EventId != eventId || row.DistanceId != distanceId || row.BibNumber != draft.Bib)
            return Result(SimulationCommitOutcomes.Skipped, "Đăng ký, cự ly hoặc BIB đã thay đổi.");
        // Durable source identity makes a retry after an uncertain response idempotent.
        if (row.RaceStatus == target && await db.RaceStatusChanges.AsNoTracking().AnyAsync(
                x => x.RegistrationId == row.Id && x.SimulationRunId == decision.RunId && x.Status == target, cancellationToken))
            return Result(SimulationCommitOutcomes.CommittedThisRun);
        var audit = row.ApplySimulationDecision(target, decision.Reason, decision.ActorId,
            decision.ChangedAtUtc, decision.RunId, decision.CheckpointId);
        var affected = await db.Registrations.Where(x => x.Id == row.Id && x.EventId == eventId
                && x.DistanceId == distanceId && x.BibNumber == draft.Bib
                && x.Status == RegistrationStatus.Confirmed && x.RaceStatus == RaceStatus.NotStarted
                && x.StartedAtUtc == null && x.FinishedAtUtc == null && x.ResultSeconds == null)
            .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.RaceStatus, target)
                .SetProperty(x => x.UpdatedAtUtc, DateTime.UtcNow), cancellationToken);
        if (affected != 1) return Result(SimulationCommitOutcomes.Retryable, "Đăng ký vừa thay đổi, hãy tải lại.");
        db.RaceStatusChanges.Add(audit);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        finally
        {
            // A failed item must not leave its audit pending in the next item's transaction.
            db.Entry(audit).State = EntityState.Detached;
        }
        return Result(SimulationCommitOutcomes.CommittedThisRun);

        SimulationCommitItemResult Result(string outcome, string? reason = null)
            => new(draft.RegistrationId, outcome, reason, null);
    }
}
