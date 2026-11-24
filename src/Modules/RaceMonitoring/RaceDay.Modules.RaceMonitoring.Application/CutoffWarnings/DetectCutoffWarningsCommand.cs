using MediatR;
using Microsoft.Extensions.Logging;
using RaceDay.Modules.Event.Contracts.Events;
using RaceDay.Modules.RaceMonitoring.Application.Abstractions;
using RaceDay.Modules.RaceMonitoring.Domain;
using RaceDay.Modules.RaceMonitoring.Domain.RunnerMonitoring;

namespace RaceDay.Modules.RaceMonitoring.Application.CutoffWarnings;

/// <summary>
/// One monitor tick. Raises a warning for each started runner with no passage at a checkpoint whose
/// cut-off (plus grace) is over, and closes active warnings once a passage shows up.
/// Server-side only; sent by the background monitor, never by HTTP.
/// </summary>
public sealed record DetectCutoffWarningsCommand(DateTime NowUtc, CutoffMonitoringSettings Settings)
    : IRequest<CutoffDetectionResult>;

public sealed record CutoffDetectionResult(int EventsScanned, int Raised, int Resolved);

public sealed class DetectCutoffWarningsCommandHandler(
    IEventCutoffScheduleQuery schedule,
    ICheckpointScanRepository scans,
    ICutoffWarningRepository warnings,
    IRaceMonitoringUnitOfWork unitOfWork,
    ICutoffWarningPublisher publisher,
    ILogger<DetectCutoffWarningsCommandHandler> logger)
    : IRequestHandler<DetectCutoffWarningsCommand, CutoffDetectionResult>
{
    private static readonly HashSet<string> OffCourseStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "Finished", "DNS", "DNF", "DQ"
    };

    public async Task<CutoffDetectionResult> Handle(DetectCutoffWarningsCommand request, CancellationToken cancellationToken)
    {
        var settings = request.Settings;
        var now = request.NowUtc;
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(now, settings.TimeZone));
        var events = await schedule.ListRaceDayEventsAsync(today.AddDays(-1), today, cancellationToken);

        int raised = 0, resolved = 0;
        foreach (var raceEvent in events)
        {
            try
            {
                var outcome = await DetectEventAsync(raceEvent, now, settings, cancellationToken);
                raised += outcome.Raised;
                resolved += outcome.Resolved;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // One broken event must not stop monitoring the others.
                logger.LogError(ex, "Không kiểm tra được cut-off của giải {EventId}.", raceEvent.EventId);
            }
        }

        return new CutoffDetectionResult(events.Count, raised, resolved);
    }

    private async Task<(int Raised, int Resolved)> DetectEventAsync(
        EventCutoffSchedule raceEvent,
        DateTime now,
        CutoffMonitoringSettings settings,
        CancellationToken cancellationToken)
    {
        var distances = raceEvent.Distances.ToDictionary(d => d.DistanceId);
        var checkpoints = raceEvent.Distances
            .SelectMany(d => d.Checkpoints.Select(c => (Distance: d, Checkpoint: c)))
            .ToDictionary(x => x.Checkpoint.CheckpointId);
        var due = DueCheckpoints(raceEvent, now, settings);
        var active = await warnings.ListActiveByEventAsync(raceEvent.EventId, cancellationToken);
        if (due.Count == 0 && active.Count == 0)
        {
            return (0, 0);
        }

        var runners = (await schedule.ListRaceRunnersAsync(raceEvent.EventId, cancellationToken))
            .Where(r => !OffCourseStatuses.Contains(r.RaceStatus))
            .ToDictionary(r => r.RegistrationId);
        var passages = (await scans.ListFirstPassagesByCheckpointsAsync(checkpoints.Keys, cancellationToken))
            .GroupBy(p => p.RegistrationId)
            .ToDictionary(g => g.Key, g => g.ToDictionary(p => p.CheckpointId, p => p.PassedAtUtc));

        var changes = new List<(CutoffWarning Warning, string Kind)>();

        foreach (var warning in active)
        {
            var reason = ResolutionFor(warning, runners, passages, checkpoints);
            if (reason is { } resolution && warning.Resolve(resolution, now))
            {
                changes.Add((warning, CutoffWarningNotificationKind.Resolved));
            }
        }

        if (due.Count > 0)
        {
            var existing = (await warnings.ListKeysByEventAsync(raceEvent.EventId, cancellationToken)).ToHashSet();
            foreach (var runner in runners.Values)
            {
                if (!distances.TryGetValue(runner.DistanceId, out var distance)
                    || !due.TryGetValue(runner.DistanceId, out var dueOnDistance))
                {
                    continue;
                }

                var passed = passages.GetValueOrDefault(runner.RegistrationId) ?? new Dictionary<Guid, DateTime>();
                if (settings.RequireStartEvidence && !HasStarted(runner, passed))
                {
                    continue;
                }

                var furthest = passed.Keys
                    .Where(checkpoints.ContainsKey)
                    .Select(id => checkpoints[id].Checkpoint.SortOrder)
                    .DefaultIfEmpty(-1)
                    .Max();
                foreach (var (checkpoint, cutoffAtUtc) in dueOnDistance)
                {
                    // A passage at a later checkpoint means this one was a missed read, not a missing runner.
                    if (passed.ContainsKey(checkpoint.CheckpointId)
                        || checkpoint.SortOrder < furthest
                        || !existing.Add(new CutoffWarningKey(runner.RegistrationId, checkpoint.CheckpointId)))
                    {
                        continue;
                    }

                    var warning = CutoffWarning.Raise(
                        new CutoffWarningTarget(
                            raceEvent.EventId,
                            raceEvent.EventName,
                            distance.DistanceId,
                            distance.Name,
                            runner.RegistrationId,
                            runner.BibNumber,
                            runner.RunnerName,
                            checkpoint.CheckpointId,
                            checkpoint.Name,
                            checkpoint.Km,
                            checkpoint.SortOrder),
                        cutoffAtUtc,
                        LastPassage(passed, checkpoints),
                        now);
                    warnings.Add(warning);
                    changes.Add((warning, CutoffWarningNotificationKind.Raised));
                }
            }
        }

        if (changes.Count == 0)
        {
            return (0, 0);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        foreach (var (warning, kind) in changes)
        {
            Publish(CutoffWarningMapper.ToNotification(warning, kind, now));
        }

        var raisedCount = changes.Count(c => c.Kind == CutoffWarningNotificationKind.Raised);
        logger.LogInformation(
            "Cut-off giải {EventId}: {Raised} cảnh báo mới, {Resolved} cảnh báo đã đóng.",
            raceEvent.EventId,
            raisedCount,
            changes.Count - raisedCount);
        return (raisedCount, changes.Count - raisedCount);
    }

    private static Dictionary<Guid, List<(EventCutoffCheckpoint Checkpoint, DateTime CutoffAtUtc)>> DueCheckpoints(
        EventCutoffSchedule raceEvent,
        DateTime now,
        CutoffMonitoringSettings settings)
    {
        var due = new Dictionary<Guid, List<(EventCutoffCheckpoint, DateTime)>>();
        foreach (var distance in raceEvent.Distances)
        {
            foreach (var checkpoint in distance.Checkpoints)
            {
                if (CutoffClock.ToUtc(checkpoint.Cutoff, raceEvent.EventDate, distance.StartAt, settings.TimeZone)
                        is not { } cutoffAtUtc
                    || cutoffAtUtc + settings.GracePeriod > now
                    || cutoffAtUtc < now - settings.LookBack)
                {
                    continue;
                }

                if (!due.TryGetValue(distance.DistanceId, out var list))
                {
                    due[distance.DistanceId] = list = [];
                }

                list.Add((checkpoint, cutoffAtUtc));
            }
        }

        return due;
    }

    private static CutoffWarningResolution? ResolutionFor(
        CutoffWarning warning,
        IReadOnlyDictionary<Guid, EventRaceRunner> runners,
        IReadOnlyDictionary<Guid, Dictionary<Guid, DateTime>> passages,
        IReadOnlyDictionary<Guid, (EventCutoffDistance Distance, EventCutoffCheckpoint Checkpoint)> checkpoints)
    {
        if (passages.TryGetValue(warning.RegistrationId, out var passed))
        {
            if (passed.ContainsKey(warning.CheckpointId))
            {
                return CutoffWarningResolution.PassedCheckpoint;
            }

            if (passed.Keys.Any(id =>
                    checkpoints.TryGetValue(id, out var cp)
                    && cp.Distance.DistanceId == warning.DistanceId
                    && cp.Checkpoint.SortOrder > warning.CheckpointSortOrder))
            {
                return CutoffWarningResolution.PassedLaterCheckpoint;
            }
        }

        // Finished, DNS, DNF, DQ or no longer confirmed: Event owns that decision now.
        return runners.ContainsKey(warning.RegistrationId) ? null : CutoffWarningResolution.RunnerStatusChanged;
    }

    private static bool HasStarted(EventRaceRunner runner, IReadOnlyDictionary<Guid, DateTime> passed)
        => passed.Count > 0
           || runner.StartedAtUtc is not null
           || runner.RaceStatus.Equals("Running", StringComparison.OrdinalIgnoreCase);

    private static CutoffWarningLastPassage? LastPassage(
        IReadOnlyDictionary<Guid, DateTime> passed,
        IReadOnlyDictionary<Guid, (EventCutoffDistance Distance, EventCutoffCheckpoint Checkpoint)> checkpoints)
    {
        var last = passed
            .Where(p => checkpoints.ContainsKey(p.Key))
            .OrderByDescending(p => p.Value)
            .Select(p => (KeyValuePair<Guid, DateTime>?)p)
            .FirstOrDefault();
        return last is { } value
            ? new CutoffWarningLastPassage(value.Key, checkpoints[value.Key].Checkpoint.Name, value.Value)
            : null;
    }

    private void Publish(CutoffWarningNotification notification)
    {
        try
        {
            publisher.Publish(notification);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Không phát được thông báo cut-off {WarningId}.", notification.WarningId);
        }
    }
}
