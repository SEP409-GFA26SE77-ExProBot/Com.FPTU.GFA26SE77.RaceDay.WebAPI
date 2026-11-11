using Microsoft.Extensions.Logging;
using RaceDay.Modules.Event.Contracts.Events;
using RaceDay.Modules.Event.Contracts.Simulation;
using RaceDay.Modules.Event.Domain.Events;
using RaceDay.Modules.Event.Domain.Registrations;
using RaceDay.Shared.Application.Abstractions;
using RaceDay.Shared.Domain.Exceptions;

namespace RaceDay.Modules.Event.Application.Simulation;

public static class SimulationFeatureFlag
{
    public static bool IsEnabled(string? value)
        => bool.TryParse(value, out var enabled) && enabled;
}

public static class SimulationOfficialStartResolver
{
    public const string UnresolvedReason =
        "Không xác định được mốc UTC. EventDate cùng StartAt/StartTime chỉ là ngày và giờ đồng hồ, không có offset. Đặt Simulation__StartTimeZone đúng nghĩa của giờ đã lưu (UTC nếu giờ đó đã là UTC). Hệ thống không tự chọn múi giờ.";

    public static bool TryResolve(
        bool enabled,
        string? timeZoneId,
        OfficialStartClocks? clocks,
        out DateTime startUtc,
        out string reason)
    {
        startUtc = default;
        if (!enabled)
        {
            reason = "Giả lập đang tắt.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(timeZoneId))
        {
            reason = UnresolvedReason;
            return false;
        }

        if (clocks is null)
        {
            reason = "Không tìm thấy giải hoặc cự ly.";
            return false;
        }

        var clock = clocks.DistanceStartAt ?? clocks.EventStartTime;
        if (clock is null)
        {
            reason = "Giải/cự ly chưa cấu hình giờ xuất phát.";
            return false;
        }

        if (!TimeZoneInfo.TryFindSystemTimeZoneById(timeZoneId.Trim(), out var zone))
        {
            reason = "Simulation__StartTimeZone không phải mã múi giờ hợp lệ.";
            return false;
        }

        var unspecified = clocks.EventDate.ToDateTime(clock.Value, DateTimeKind.Unspecified);
        if (IsUtcZone(zone))
        {
            startUtc = DateTime.SpecifyKind(unspecified, DateTimeKind.Utc);
            reason = "";
            return true;
        }

        startUtc = TimeZoneInfo.ConvertTimeToUtc(unspecified, zone);
        reason = "";
        return true;
    }

    private static bool IsUtcZone(TimeZoneInfo zone)
        => zone.Equals(TimeZoneInfo.Utc)
           || zone.Id is "UTC" or "Etc/UTC" or "Etc/UCT" or "UCT" or "Coordinated Universal Time";
}

/// <summary>Null means the row is still eligible to write. A non-null value is the outcome, with no write.</summary>
public static class SimulationCommitClassifier
{
    public static bool HasStoredResult(SimulationRegistrationState row)
        => row.ResultSeconds is not null
           || row.FinishedAtUtc is not null
           || row.Status == nameof(RegistrationStatus.Finished)
           || row.RaceStatus == nameof(RaceStatus.Finished);

    public static string? Classify(SimulationRegistrationState? row, Guid eventId, Guid distanceId)
    {
        if (row is null)
        {
            return null;
        }

        if (row.EventId != eventId || row.DistanceId != distanceId)
        {
            return SimulationCommitOutcomes.Skipped;
        }

        if (HasStoredResult(row))
        {
            return SimulationCommitOutcomes.AlreadyRecordedElsewhere;
        }

        if (row.Status != nameof(RegistrationStatus.Confirmed)
            || row.RaceStatus is nameof(RaceStatus.DNS) or nameof(RaceStatus.DNF) or nameof(RaceStatus.DQ)
            || row.StartedAtUtc is not null
            || row.RaceStatus != nameof(RaceStatus.NotStarted)
            || string.IsNullOrWhiteSpace(row.Bib))
        {
            return SimulationCommitOutcomes.Skipped;
        }

        return null;
    }
}

public interface ISimulationFinishWriter
{
    /// <summary>
    /// Returns 1 when the conditional update wrote the finish.
    /// eventId, distanceId and bib are part of the update predicate.
    /// bib is the simulation run's original bib, not a later value read from the registration.
    /// Does not set StartedAtUtc.
    /// </summary>
    Task<int> WriteAsync(
        Guid registrationId,
        Guid eventId,
        Guid distanceId,
        string bib,
        DateTime finishUtc,
        long resultSeconds,
        CancellationToken cancellationToken = default);
}

public interface ISimulationPassageWriter
{
    /// <summary>
    /// Persists checkpoint splits after a successful finish write.
    /// Resolves EventCheckpoint by ExternalId when possible; otherwise uses a stable Guid from distance+externalId.
    /// Idempotent per (registration, checkpoint).
    /// </summary>
    Task WriteAsync(
        Guid registrationId,
        Guid eventId,
        Guid distanceId,
        DateTime startUtc,
        IReadOnlyList<SimulationSplitDraft> splits,
        CancellationToken cancellationToken = default);
}

public sealed class SimulationResultCommitter(
    ISimulationDemoGate gate,
    ISimulationOfficialStartSource starts,
    ISimulationCandidateQuery candidates,
    ISimulationFinishWriter writer,
    ISimulationPassageWriter passages,
    ICurrentUserAccessor currentUser,
    IEventSosOpsAccessQuery access,
    ILogger<SimulationResultCommitter> logger) : ISimulationResultCommitPort
{
    public const string ChangedDistanceOrEventReason =
        "Đăng ký đã đổi event hoặc cự ly sau khi kiểm tra. Không ghi với dữ liệu mới.";

    public const string ChangedBibReason =
        "Đăng ký đã đổi BIB sau khi kiểm tra. Không ghi với dữ liệu mới.";

    public const string ResultSourceUnknownReason =
        "Đã có kết quả nhưng không xác định được nguồn ghi. Giữ nguyên dữ liệu.";

    public async Task<SimulationCommitBatch> CommitAsync(
        SimulationCommitCommand command,
        CancellationToken cancellationToken = default)
    {
        var userId = currentUser.UserId ?? throw new DomainException("Cần đăng nhập.");
        if (!await access.CanManageSosAsync(command.EventId, userId, cancellationToken))
        {
            throw new DomainException("Bạn không có quyền ghi nhận kết quả của giải này.");
        }

        var clocks = await starts.GetClocksAsync(command.EventId, command.DistanceId, cancellationToken);
        if (!SimulationOfficialStartResolver.TryResolve(gate.Enabled, gate.StartTimeZone, clocks, out var startUtc, out var reason))
        {
            return new SimulationCommitBatch(false, reason, []);
        }

        var committed = new HashSet<Guid>(command.AlreadyCommittedByThisRun);
        var unresolved = new HashSet<Guid>(command.UnresolvedRegistrationIds ?? []);
        var items = new List<SimulationCommitItemResult>(command.Drafts.Count);
        foreach (var draft in command.Drafts)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                items.Add(unresolved.Contains(draft.RegistrationId)
                    ? Unknown(draft.RegistrationId)
                    : Retryable(draft.RegistrationId));
                continue;
            }

            if (committed.Contains(draft.RegistrationId))
            {
                items.Add(new SimulationCommitItemResult(
                    draft.RegistrationId, SimulationCommitOutcomes.CommittedThisRun, null, null));
                continue;
            }

            items.Add(await CommitOneAsync(
                command,
                draft,
                unresolved.Contains(draft.RegistrationId),
                startUtc,
                cancellationToken));
        }

        return new SimulationCommitBatch(true, null, items);
    }

    private async Task<SimulationCommitItemResult> CommitOneAsync(
        SimulationCommitCommand command,
        SimulationFinishDraft draft,
        bool unresolved,
        DateTime startUtc,
        CancellationToken cancellationToken)
    {
        var writeSent = false;
        try
        {
            SimulationRegistrationState? row;
            try
            {
                row = await candidates.GetAsync(draft.RegistrationId, cancellationToken);
            }
            catch (Exception ex) when (ex is not DomainException)
            {
                logger.LogError(ex, "Simulation finish read failed for registration {RegistrationId}", draft.RegistrationId);
                return unresolved ? Unknown(draft.RegistrationId) : Retryable(draft.RegistrationId);
            }

            if (row is null)
            {
                return Skipped(draft.RegistrationId, "Không tìm thấy đăng ký.");
            }

            if (unresolved && SimulationCommitClassifier.HasStoredResult(row))
            {
                return Unknown(draft.RegistrationId, ResultSourceUnknownReason);
            }

            var classified = SimulationCommitClassifier.Classify(row, command.EventId, command.DistanceId);
            if (classified is not null)
            {
                return new SimulationCommitItemResult(draft.RegistrationId, classified, ReasonFor(classified), null);
            }

            if (!string.Equals(row.Bib, draft.Bib, StringComparison.Ordinal))
            {
                return Skipped(draft.RegistrationId, ChangedBibReason);
            }

            long seconds;
            try
            {
                seconds = ResultTime.RoundElapsed(draft.ElapsedSeconds);
            }
            catch (DomainException ex)
            {
                return Skipped(draft.RegistrationId, ex.Message);
            }

            var finishUtc = DateTime.SpecifyKind(startUtc.AddSeconds(seconds), DateTimeKind.Utc);
            var stored = ResultTime.RoundBetween(startUtc, finishUtc);
            writeSent = true;
            var written = await writer.WriteAsync(
                draft.RegistrationId,
                command.EventId,
                command.DistanceId,
                draft.Bib,
                finishUtc,
                stored,
                cancellationToken);
            if (written == 1)
            {
                if (draft.Splits is { Count: > 0 })
                {
                    try
                    {
                        await passages.WriteAsync(
                            draft.RegistrationId,
                            command.EventId,
                            command.DistanceId,
                            startUtc,
                            draft.Splits,
                            cancellationToken);
                    }
                    catch (Exception ex) when (ex is not DomainException)
                    {
                        logger.LogError(
                            ex,
                            "Simulation finish committed but checkpoint passages failed for registration {RegistrationId}",
                            draft.RegistrationId);
                    }
                }

                return new SimulationCommitItemResult(
                    draft.RegistrationId, SimulationCommitOutcomes.CommittedThisRun, null, stored);
            }

            SimulationRegistrationState? again;
            try
            {
                again = await candidates.GetAsync(draft.RegistrationId, cancellationToken);
            }
            catch (Exception ex) when (ex is not DomainException)
            {
                logger.LogError(ex, "Simulation finish reread failed for registration {RegistrationId}", draft.RegistrationId);
                return unresolved ? Unknown(draft.RegistrationId) : Retryable(draft.RegistrationId);
            }

            if (again is null)
            {
                return Skipped(draft.RegistrationId, "Không tìm thấy đăng ký.");
            }

            var identity = ChangedIdentityReason(row, again, command.EventId, command.DistanceId);
            if (identity is not null)
            {
                return Skipped(draft.RegistrationId, identity);
            }

            if (unresolved && SimulationCommitClassifier.HasStoredResult(again))
            {
                return Unknown(draft.RegistrationId, ResultSourceUnknownReason);
            }

            var after = SimulationCommitClassifier.Classify(again, command.EventId, command.DistanceId);
            var outcome = after ?? SimulationCommitOutcomes.Retryable;
            return new SimulationCommitItemResult(draft.RegistrationId, outcome, ReasonFor(outcome), null);
        }
        catch (DomainException ex)
        {
            return Skipped(draft.RegistrationId, ex.Message);
        }
        catch (Exception ex)
        {
            if (writeSent)
            {
                logger.LogError(
                    ex,
                    "Simulation finish write outcome is unknown for registration {RegistrationId}",
                    draft.RegistrationId);
                return Unknown(draft.RegistrationId);
            }

            logger.LogError(ex, "Simulation finish read failed for registration {RegistrationId}", draft.RegistrationId);
            return unresolved ? Unknown(draft.RegistrationId) : Retryable(draft.RegistrationId);
        }
    }

    private static string? ChangedIdentityReason(
        SimulationRegistrationState verified,
        SimulationRegistrationState current,
        Guid eventId,
        Guid distanceId)
    {
        if (current.EventId != eventId || current.DistanceId != distanceId)
        {
            return ChangedDistanceOrEventReason;
        }

        if (!string.Equals(current.Bib, verified.Bib, StringComparison.Ordinal))
        {
            return ChangedBibReason;
        }

        return null;
    }

    private static SimulationCommitItemResult Unknown(Guid registrationId, string? reason = null)
        => new(registrationId, SimulationCommitOutcomes.Unknown, reason ?? ReasonFor(SimulationCommitOutcomes.Unknown), null);

    private static SimulationCommitItemResult Retryable(Guid registrationId)
        => new(registrationId, SimulationCommitOutcomes.Retryable, ReasonFor(SimulationCommitOutcomes.Retryable), null);

    private static SimulationCommitItemResult Skipped(Guid registrationId, string reason)
        => new(registrationId, SimulationCommitOutcomes.Skipped, reason, null);

    private static string ReasonFor(string outcome) => outcome switch
    {
        SimulationCommitOutcomes.AlreadyRecordedElsewhere => "Đăng ký đã có kết quả hoặc đã hoàn thành.",
        SimulationCommitOutcomes.Skipped => "Đăng ký không còn đủ điều kiện ghi finish demo.",
        SimulationCommitOutcomes.Retryable => "Chưa ghi được. Có thể thử lại.",
        SimulationCommitOutcomes.Unknown => "Không rõ đã ghi hay chưa. Hãy đọc lại đăng ký trước khi thử lại.",
        _ => ""
    };
}
