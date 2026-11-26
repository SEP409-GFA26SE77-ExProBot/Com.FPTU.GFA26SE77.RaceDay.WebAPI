using RaceDay.Modules.RaceMonitoring.Contracts.Timeline;
using RaceDay.Modules.Simulation.Application;
using RaceDay.Modules.Simulation.Contracts;

namespace RaceDay.Modules.Simulation.UnitTests;

public class SimulationTimelineFeedTests
{
    private static readonly Guid EventId = Guid.NewGuid();
    private static readonly Guid DistanceId = Guid.NewGuid();
    private static readonly Guid RunnerA = Guid.NewGuid();
    private static readonly Guid RunnerB = Guid.NewGuid();

    private static SimulationAthleteDto Athlete(Guid registrationId, string bib, params SimulationSplitDto[] splits)
        => new(registrationId, bib, "VĐV " + bib, "chip-" + bib, 10, 10, 0, "Racing", false, false, null, null, splits);

    private static SimulationSnapshotDto Snapshot(Guid runId, params SimulationAthleteDto[] athletes)
        => new(runId, 1, 1, EventId, DistanceId, "Running", "Idle", 7, 1, 0, 5, "5KM", false,
            [], [], [], athletes, [], [], []);

    private static readonly SimulationSplitDto Start = new("cp-0", "Xuất phát", 0, 0);
    private static readonly SimulationSplitDto Cp = new("cp-1.535", "CP 0.7km", 1.535, 433.654248);

    private static List<SimulationTimelineWork> Drain(SimulationTimelineFeed feed)
    {
        var items = new List<SimulationTimelineWork>();
        while (feed.Reader.TryRead(out var item))
        {
            items.Add(item);
        }

        return items;
    }

    [Fact]
    public async Task PublishAsync_FirstSnapshotOfRun_AnnouncesRunAndQueuesItsSplits()
    {
        var feed = new SimulationTimelineFeed();
        var run = Guid.NewGuid();

        await feed.PublishAsync(Snapshot(run, Athlete(RunnerA, "50401", Start), Athlete(RunnerB, "50402", Start)));

        var work = Drain(feed);
        Assert.Equal(new SimulationTimelineRunStarted(EventId, DistanceId, run), work[0]);
        var passages = Assert.IsType<SimulationTimelinePassages>(work[1]).Passages;
        Assert.Equal(["50401", "50402"], passages.Select(p => p.Bib));
        Assert.All(passages, p => Assert.Equal("cp-0", p.CourseCheckpointId));
    }

    [Fact]
    public async Task PublishAsync_LaterSnapshots_QueueOnlyNewCrossings_WithExactSimTime()
    {
        var feed = new SimulationTimelineFeed();
        var run = Guid.NewGuid();
        await feed.PublishAsync(Snapshot(run, Athlete(RunnerA, "50401", Start)));
        Drain(feed);

        await feed.PublishAsync(Snapshot(run, Athlete(RunnerA, "50401", Start)));
        Assert.Empty(Drain(feed));

        await feed.PublishAsync(Snapshot(run, Athlete(RunnerA, "50401", Start, Cp)));
        var passage = Assert.Single(Assert.IsType<SimulationTimelinePassages>(Assert.Single(Drain(feed))).Passages);
        Assert.Equal(new SimulatedCheckpointPassage(run, EventId, DistanceId, RunnerA, "50401", "VĐV 50401",
            "cp-1.535", "CP 0.7km", 433.654248), passage);
    }

    [Fact]
    public async Task PublishAsync_ReplacedRun_StartsOverAndIgnoresTheOldRun()
    {
        var feed = new SimulationTimelineFeed();
        var oldRun = Guid.NewGuid();
        var newRun = Guid.NewGuid();
        await feed.PublishAsync(Snapshot(oldRun, Athlete(RunnerA, "50401", Start)));
        Drain(feed);

        await feed.PublishAsync(Snapshot(newRun, Athlete(RunnerA, "50401", Start)));
        var work = Drain(feed);
        Assert.Equal(new SimulationTimelineRunStarted(EventId, DistanceId, newRun), work[0]);
        Assert.Equal(newRun, Assert.Single(Assert.IsType<SimulationTimelinePassages>(work[1]).Passages).RunId);

        await feed.PublishAsync(Snapshot(oldRun, Athlete(RunnerA, "50401", Start, Cp)));
        Assert.Empty(Drain(feed));
    }

    [Fact]
    public async Task CompositeBroadcaster_ForwardsToEveryBroadcasterInOrder()
    {
        var calls = new List<string>();
        var composite = new CompositeSimulationBroadcaster(new Recording("signalr", calls), new Recording("timeline", calls));

        await composite.PublishAsync(Snapshot(Guid.NewGuid()));
        await composite.PublishReplacedAsync(new SimulationRunReplacedDto(Guid.NewGuid(), 1, Guid.NewGuid(), 2, 1));

        Assert.Equal(["signalr:snapshot", "timeline:snapshot", "signalr:replaced", "timeline:replaced"], calls);
    }

    private sealed class Recording(string name, List<string> calls) : ISimulationBroadcaster
    {
        public Task PublishAsync(SimulationSnapshotDto snapshot, CancellationToken cancellationToken = default)
        {
            calls.Add(name + ":snapshot");
            return Task.CompletedTask;
        }

        public Task PublishReplacedAsync(SimulationRunReplacedDto notice, CancellationToken cancellationToken = default)
        {
            calls.Add(name + ":replaced");
            return Task.CompletedTask;
        }
    }
}
