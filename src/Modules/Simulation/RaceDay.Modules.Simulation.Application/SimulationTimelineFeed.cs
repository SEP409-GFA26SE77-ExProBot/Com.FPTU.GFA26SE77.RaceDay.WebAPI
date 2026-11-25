using System.Threading.Channels;
using RaceDay.Modules.RaceMonitoring.Contracts.Timeline;
using RaceDay.Modules.Simulation.Contracts;

namespace RaceDay.Modules.Simulation.Application;

public abstract record SimulationTimelineWork;

/// <summary>New checkpoint crossings found in a snapshot.</summary>
public sealed record SimulationTimelinePassages(IReadOnlyList<SimulatedCheckpointPassage> Passages) : SimulationTimelineWork;

/// <summary>First snapshot of a run on a distance (create, Reset, New Run, or after a restart): drop other runs' rows.</summary>
public sealed record SimulationTimelineRunStarted(Guid EventId, Guid DistanceId, Guid RunId) : SimulationTimelineWork;

/// <summary>
/// Turns simulator snapshots into timeline work without touching the race clock: compares each snapshot's splits
/// with what was already queued and enqueues only new crossings. Never blocks the caller; a background worker
/// writes the queue to RaceMonitoring.
/// </summary>
public sealed class SimulationTimelineFeed : ISimulationBroadcaster
{
    private readonly object _gate = new();
    private readonly Dictionary<(Guid EventId, Guid DistanceId), Guid> _currentRun = new();
    private readonly Dictionary<Guid, HashSet<(Guid RegistrationId, string CheckpointId)>> _queued = new();
    private readonly HashSet<Guid> _retired = new();
    private readonly Channel<SimulationTimelineWork> _work = Channel.CreateUnbounded<SimulationTimelineWork>(
        new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });

    public ChannelReader<SimulationTimelineWork> Reader => _work.Reader;

    public Task PublishAsync(SimulationSnapshotDto snapshot, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        lock (_gate)
        {
            if (_retired.Contains(snapshot.RunId))
            {
                return Task.CompletedTask;
            }

            var slot = (snapshot.EventId, snapshot.DistanceId);
            if (!_currentRun.TryGetValue(slot, out var current) || current != snapshot.RunId)
            {
                if (_currentRun.ContainsKey(slot))
                {
                    _queued.Remove(current);
                    _retired.Add(current);
                }

                _currentRun[slot] = snapshot.RunId;
                _queued[snapshot.RunId] = [];
                _work.Writer.TryWrite(new SimulationTimelineRunStarted(snapshot.EventId, snapshot.DistanceId, snapshot.RunId));
            }

            var queued = _queued[snapshot.RunId];
            List<SimulatedCheckpointPassage>? batch = null;
            foreach (var athlete in snapshot.Athletes)
            {
                foreach (var split in athlete.Splits)
                {
                    if (!queued.Add((athlete.RegistrationId, split.CheckpointId)))
                    {
                        continue;
                    }

                    (batch ??= []).Add(new SimulatedCheckpointPassage(
                        snapshot.RunId,
                        snapshot.EventId,
                        snapshot.DistanceId,
                        athlete.RegistrationId,
                        athlete.Bib,
                        athlete.FullName,
                        split.CheckpointId,
                        split.Name,
                        split.SimSeconds));
                }
            }

            if (batch is not null)
            {
                _work.Writer.TryWrite(new SimulationTimelinePassages(batch));
            }
        }

        return Task.CompletedTask;
    }

    /// <summary>The replacing run's first snapshot follows and triggers the cleanup.</summary>
    public Task PublishReplacedAsync(SimulationRunReplacedDto notice, CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}

/// <summary>Sends every snapshot to each broadcaster in order (SignalR clients, then the timeline feed).</summary>
public sealed class CompositeSimulationBroadcaster(params ISimulationBroadcaster[] broadcasters) : ISimulationBroadcaster
{
    public async Task PublishAsync(SimulationSnapshotDto snapshot, CancellationToken cancellationToken = default)
    {
        foreach (var broadcaster in broadcasters)
        {
            await broadcaster.PublishAsync(snapshot, cancellationToken);
        }
    }

    public async Task PublishReplacedAsync(SimulationRunReplacedDto notice, CancellationToken cancellationToken = default)
    {
        foreach (var broadcaster in broadcasters)
        {
            await broadcaster.PublishReplacedAsync(notice, cancellationToken);
        }
    }
}
