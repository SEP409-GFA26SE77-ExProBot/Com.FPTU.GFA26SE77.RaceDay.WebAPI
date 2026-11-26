using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using RaceDay.Modules.Event.Contracts.Events;
using RaceDay.Modules.Event.Contracts.Simulation;
using RaceDay.Modules.RaceMonitoring.Application.Abstractions;
using RaceDay.Modules.RaceMonitoring.Contracts.Timeline;
using RaceDay.Modules.RaceMonitoring.Domain;

namespace RaceDay.Modules.RaceMonitoring.Application.RaceEventLogs;

/// <summary>
/// Process-wide state of the simulation feed: the race-clock anchor of each run (so every row of a run uses
/// the same origin) and which unknown checkpoints were already reported.
/// </summary>
public sealed class SimulationRunAnchorCache
{
    private readonly ConcurrentDictionary<Guid, (Guid EventId, Guid DistanceId, DateTime AnchorUtc)> _runs = new();
    private readonly ConcurrentDictionary<(Guid RunId, string CheckpointId), byte> _warned = new();

    public bool TryGet(Guid runId, out DateTime anchorUtc)
    {
        if (_runs.TryGetValue(runId, out var run))
        {
            anchorUtc = run.AnchorUtc;
            return true;
        }

        anchorUtc = default;
        return false;
    }

    public DateTime GetOrAdd(Guid runId, Guid eventId, Guid distanceId, DateTime anchorUtc)
        => _runs.GetOrAdd(runId, (eventId, distanceId, anchorUtc)).AnchorUtc;

    public bool FirstWarning(Guid runId, string checkpointId) => _warned.TryAdd((runId, checkpointId), 0);

    public void ForgetOthers(Guid eventId, Guid distanceId, Guid currentRunId)
    {
        foreach (var (runId, run) in _runs)
        {
            if (runId != currentRunId && run.EventId == eventId && run.DistanceId == distanceId)
            {
                _runs.TryRemove(runId, out _);
            }
        }

        foreach (var key in _warned.Keys)
        {
            if (!_runs.ContainsKey(key.RunId) && key.RunId != currentRunId)
            {
                _warned.TryRemove(key, out _);
            }
        }
    }
}

/// <summary>
/// Writes simulator checkpoint crossings as timeline rows (RFID, marked simulated) and pushes them over SSE.
/// OccurredAtUtc = official start (event date + distance start, Simulation:StartTimeZone — the same anchor the
/// result commit uses) + simulated crossing time. No checkpoint scan is created, so cut-off monitoring ignores them.
/// </summary>
public sealed class SimulationTimelineSink(
    IEventCheckpointQuery checkpoints,
    ISimulationOfficialStartSource starts,
    ISimulationDemoGate gate,
    IRaceEventLogRepository logs,
    IRaceMonitoringUnitOfWork unitOfWork,
    IRaceEventLogPublisher publisher,
    RaceEventLogChipStarts chipStarts,
    SimulationRunAnchorCache anchors,
    ILogger<SimulationTimelineSink> logger) : ISimulationTimelineSink
{
    public async Task RecordAsync(
        IReadOnlyList<SimulatedCheckpointPassage> passages,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(passages);
        foreach (var run in passages.GroupBy(passage => (passage.RunId, passage.EventId, passage.DistanceId)))
        {
            await RecordRunAsync(run.Key.RunId, run.Key.EventId, run.Key.DistanceId, run.ToList(), cancellationToken);
        }
    }

    public async Task ClearOtherRunsAsync(
        Guid eventId,
        Guid distanceId,
        Guid currentRunId,
        CancellationToken cancellationToken = default)
    {
        var removed = await logs.DeleteOtherSimulationRunsAsync(eventId, distanceId, currentRunId, cancellationToken);
        anchors.ForgetOthers(eventId, distanceId, currentRunId);
        if (removed == 0)
        {
            return;
        }

        logger.LogInformation(
            "Đã xóa {Count} dòng timeline giả lập cũ của cự ly {DistanceId} (giải {EventId}).",
            removed,
            distanceId,
            eventId);
        try
        {
            publisher.Publish(RaceEventLogNotification.SimulationCleared(eventId, distanceId, currentRunId));
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Không phát được SSE xóa timeline giả lập của giải {EventId}.", eventId);
        }
    }

    /// <summary>Stable per (run, runner, course checkpoint): a resend after a retry is skipped, a new run gets new ids.</summary>
    public static Guid PassageId(Guid runId, Guid registrationId, string courseCheckpointId)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"simulation:{runId:N}:{registrationId:N}:{courseCheckpointId}"));
        return new Guid(hash.AsSpan(0, 16));
    }

    public static bool TryResolveOfficialStart(string? timeZoneId, OfficialStartClocks? clocks, out DateTime startUtc)
    {
        startUtc = default;
        var clock = clocks?.DistanceStartAt ?? clocks?.EventStartTime;
        if (clocks is null || clock is null || string.IsNullOrWhiteSpace(timeZoneId)
            || !TimeZoneInfo.TryFindSystemTimeZoneById(timeZoneId.Trim(), out var zone))
        {
            return false;
        }

        var local = clocks.EventDate.ToDateTime(clock.Value, DateTimeKind.Unspecified);
        startUtc = DateTime.SpecifyKind(TimeZoneInfo.ConvertTimeToUtc(local, zone), DateTimeKind.Utc);
        return true;
    }

    public static TimeSpan SimTime(double seconds)
        => TimeSpan.FromTicks((long)Math.Round(seconds * TimeSpan.TicksPerSecond, MidpointRounding.AwayFromZero));

    private async Task RecordRunAsync(
        Guid runId,
        Guid eventId,
        Guid distanceId,
        IReadOnlyList<SimulatedCheckpointPassage> items,
        CancellationToken cancellationToken)
    {
        var byExternalId = (await checkpoints.ListByEventAsync(eventId, cancellationToken))
            .Where(checkpoint => checkpoint.DistanceId == distanceId && !string.IsNullOrWhiteSpace(checkpoint.ExternalId))
            .GroupBy(checkpoint => checkpoint.ExternalId!, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);

        var candidates = new List<(SimulatedCheckpointPassage Passage, EventCheckpointInfo Checkpoint, Guid SourceId)>();
        foreach (var passage in items)
        {
            if (!byExternalId.TryGetValue(passage.CourseCheckpointId, out var checkpoint))
            {
                if (anchors.FirstWarning(runId, passage.CourseCheckpointId))
                {
                    logger.LogWarning(
                        "Checkpoint {CheckpointId} của course chưa có trong event_checkpoints (cự ly {DistanceId}); "
                        + "bỏ qua lượt qua checkpoint giả lập. Organizer lưu lại bản đồ để đồng bộ checkpoint.",
                        passage.CourseCheckpointId,
                        distanceId);
                }

                continue;
            }

            candidates.Add((passage, checkpoint, PassageId(runId, passage.RegistrationId, passage.CourseCheckpointId)));
        }

        if (candidates.Count == 0)
        {
            return;
        }

        var anchor = await AnchorAsync(runId, eventId, distanceId, items.Min(passage => passage.SimSeconds), cancellationToken);
        var seen = (await logs.ListExistingSourceIdsAsync(
                RaceEventLogSource.Simulation,
                candidates.Select(candidate => candidate.SourceId).ToList(),
                cancellationToken))
            .ToHashSet();
        var recordedAtUtc = DateTime.UtcNow;
        var created = new List<RaceEventLog>();
        foreach (var (passage, checkpoint, sourceId) in candidates)
        {
            if (!seen.Add(sourceId))
            {
                continue;
            }

            var log = RaceEventLog.RecordSimulatedCheckpointPassed(
                eventId,
                distanceId,
                anchor + SimTime(passage.SimSeconds),
                recordedAtUtc,
                passage.Bib,
                checkpoint.CheckpointId,
                passage.RegistrationId,
                sourceId,
                RaceEventLogMapper.SerializeCheckpointMetadata(
                    nameof(ScanMethod.RFID),
                    string.IsNullOrWhiteSpace(checkpoint.CheckpointName) ? passage.CheckpointName : checkpoint.CheckpointName,
                    passage.RunnerName,
                    runId));
            logs.Add(log);
            created.Add(log);
        }

        if (created.Count == 0)
        {
            return;
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        IReadOnlyList<RaceEventLogNotification> notifications;
        try
        {
            notifications = await chipStarts.ToNotificationsAsync(eventId, created, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Không đọc được giờ xuất phát chip cho lượt giả lập {RunId}.", runId);
            notifications = created.Select(log => RaceEventLogMapper.ToNotification(log)).ToList();
        }

        foreach (var notification in notifications.OrderBy(item => item.OccurredAtUtc).ThenBy(item => item.Sequence))
        {
            try
            {
                publisher.Publish(notification);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Không phát được SSE cho log giả lập {LogId}.", notification.Id);
            }
        }
    }

    private async Task<DateTime> AnchorAsync(
        Guid runId,
        Guid eventId,
        Guid distanceId,
        double firstSimSeconds,
        CancellationToken cancellationToken)
    {
        if (anchors.TryGet(runId, out var known))
        {
            return known;
        }

        var clocks = await starts.GetClocksAsync(eventId, distanceId, cancellationToken);
        if (!TryResolveOfficialStart(gate.StartTimeZone, clocks, out var anchor))
        {
            anchor = DateTime.UtcNow - SimTime(firstSimSeconds);
            logger.LogInformation(
                "Không xác định được giờ xuất phát chính thức cho lượt giả lập {RunId}; dùng giờ hệ thống làm mốc.",
                runId);
        }

        return anchors.GetOrAdd(runId, eventId, distanceId, anchor);
    }
}
