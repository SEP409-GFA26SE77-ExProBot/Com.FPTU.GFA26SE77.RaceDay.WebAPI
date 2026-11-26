using RaceDay.Modules.Event.Contracts.Events;
using RaceDay.Modules.RaceMonitoring.Application.Abstractions;
using RaceDay.Modules.RaceMonitoring.Domain;

namespace RaceDay.Modules.RaceMonitoring.Application.RaceEventLogs;

/// <summary>
/// Chip time origin: a runner's first pass at a Start checkpoint. The timeline shows
/// OccurredAtUtc − ChipStartAtUtc. Null when the runner has no Start pass yet.
/// </summary>
public sealed class RaceEventLogChipStarts(IEventCheckpointQuery checkpoints, IRaceEventLogRepository logs)
{
    public const string StartCheckpointType = "Start";

    /// <summary>Chip start per log id for the given rows of one event.</summary>
    public async Task<IReadOnlyDictionary<Guid, DateTime>> ResolveAsync(
        Guid eventId,
        IReadOnlyCollection<RaceEventLog> items,
        CancellationToken cancellationToken = default)
    {
        var passes = items
            .Where(log => log.EventType == RaceEventLogType.RunnerCheckpointPassed && log.RegistrationId.HasValue)
            .ToList();
        if (passes.Count == 0)
        {
            return new Dictionary<Guid, DateTime>();
        }

        var startIds = (await checkpoints.ListByEventAsync(eventId, cancellationToken))
            .Where(checkpoint => string.Equals(checkpoint.Type, StartCheckpointType, StringComparison.OrdinalIgnoreCase))
            .Select(checkpoint => checkpoint.CheckpointId)
            .ToList();
        if (startIds.Count == 0)
        {
            return new Dictionary<Guid, DateTime>();
        }

        var registrationIds = passes.Select(log => log.RegistrationId!.Value).Distinct().ToList();
        var starts = (await logs.ListChipStartsAsync(eventId, startIds, registrationIds, cancellationToken))
            .ToDictionary(row => (row.RegistrationId, row.Simulated), row => row.StartedAtUtc);

        var result = new Dictionary<Guid, DateTime>();
        foreach (var log in passes)
        {
            var key = (log.RegistrationId!.Value, log.SourceType == RaceEventLogSource.Simulation);
            if (starts.TryGetValue(key, out var startedAtUtc))
            {
                result[log.Id] = startedAtUtc;
            }
        }

        return result;
    }

    public async Task<IReadOnlyList<RaceEventLogNotification>> ToNotificationsAsync(
        Guid eventId,
        IReadOnlyCollection<RaceEventLog> items,
        CancellationToken cancellationToken = default)
    {
        var starts = await ResolveAsync(eventId, items, cancellationToken);
        return items
            .Select(log => RaceEventLogMapper.ToNotification(
                log,
                starts.TryGetValue(log.Id, out var startedAtUtc) ? startedAtUtc : null))
            .ToList();
    }
}
