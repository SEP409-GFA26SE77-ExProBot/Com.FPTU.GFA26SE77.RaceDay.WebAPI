using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using RaceDay.Modules.Event.Contracts.Events;
using RaceDay.Modules.Event.Contracts.Simulation;
using RaceDay.Modules.RaceMonitoring.Application.Abstractions;
using RaceDay.Modules.RaceMonitoring.Application.RaceEventLogs;
using RaceDay.Modules.RaceMonitoring.Contracts.Timeline;
using RaceDay.Modules.RaceMonitoring.Domain;

namespace RaceDay.Modules.RaceMonitoring.UnitTests.Application;

public class SimulationTimelineSinkTests
{
    private static readonly Guid EventId = Guid.Parse("c0ffeeee-0001-4000-8000-000000000001");
    private static readonly Guid DistanceId = Guid.Parse("c0ffeeee-0001-4000-8000-0000000000d1");
    private static readonly Guid StartId = Guid.Parse("c0ffeeee-0003-4000-8000-000000000001");
    private static readonly Guid CpId = Guid.Parse("c0ffeeee-0003-4000-8000-000000000002");
    private static readonly Guid RunId = Guid.Parse("45c6d26a-d8da-400c-aefa-bb77424e0872");
    private static readonly Guid RegistrationId = Guid.Parse("c0ffeeee-0005-4000-8000-000000000001");
    private static readonly DateTime OfficialStartUtc = new(2027, 12, 1, 6, 0, 0, DateTimeKind.Utc);

    private static SimulatedCheckpointPassage Passage(string courseCheckpointId, double simSeconds, Guid? registrationId = null)
        => new(RunId, EventId, DistanceId, registrationId ?? RegistrationId, "50401", "VĐV Trail 01",
            courseCheckpointId, courseCheckpointId, simSeconds);

    [Fact]
    public async Task RecordAsync_WritesRfidSimulatedRows_OnRaceClockWithMicroseconds()
    {
        var h = Harness();
        h.Logs.ListChipStartsAsync(EventId, Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns([new RaceEventLogChipStart(RegistrationId, true, OfficialStartUtc)]);

        await h.Sink.RecordAsync([Passage("cp-0", 0), Passage("cp-1.535", 433.654248)]);

        Assert.Equal(2, h.Added.Count);
        var cp = h.Added.Single(log => log.CheckpointId == CpId);
        Assert.Equal(RaceEventLogSource.Simulation, cp.SourceType);
        Assert.Equal(SimulationTimelineSink.PassageId(RunId, RegistrationId, "cp-1.535"), cp.SourceId);
        Assert.Equal(OfficialStartUtc.AddTicks(4_336_542_480), cp.OccurredAtUtc);
        var metadata = RaceEventLogMapper.ReadMetadata(cp.Metadata);
        Assert.Equal("RFID", metadata.ScanMethod);
        Assert.Equal("CP 0.7km", metadata.CheckpointName);
        Assert.Equal(RunId, metadata.SimulationRunId);
        await h.UnitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());

        Assert.Equal(2, h.Published.Count);
        Assert.All(h.Published, notification =>
        {
            Assert.True(notification.Simulated);
            Assert.Equal(RunId, notification.SimulationRunId);
            Assert.Equal(OfficialStartUtc, notification.ChipStartAtUtc);
            Assert.Equal("RFID", notification.ScanMethod);
        });
        var published = h.Published.Single(notification => notification.CheckpointId == CpId);
        Assert.Equal(TimeSpan.FromTicks(4_336_542_480), published.OccurredAtUtc - published.ChipStartAtUtc!.Value);
    }

    [Fact]
    public async Task RecordAsync_SkipsUnknownCheckpointsAndRowsAlreadyWritten()
    {
        var h = Harness();
        var existing = SimulationTimelineSink.PassageId(RunId, RegistrationId, "cp-0");
        h.Logs.ListExistingSourceIdsAsync(RaceEventLogSource.Simulation, Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns([existing]);

        await h.Sink.RecordAsync([Passage("cp-0", 0), Passage("cp-missing", 100), Passage("cp-0", 0)]);

        Assert.Empty(h.Added);
        Assert.Empty(h.Published);
        await h.UnitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RecordAsync_WithoutOfficialStart_AnchorsOnServerClock()
    {
        var h = Harness(clocks: null);
        var before = DateTime.UtcNow;

        await h.Sink.RecordAsync([Passage("cp-0", 0)]);

        var log = Assert.Single(h.Added);
        Assert.InRange(log.OccurredAtUtc, before.AddSeconds(-1), DateTime.UtcNow.AddSeconds(1));
    }

    [Fact]
    public async Task RecordAsync_SameRun_UsesOneAnchorForEveryBatch()
    {
        var h = Harness();

        await h.Sink.RecordAsync([Passage("cp-0", 0)]);
        h.Starts.GetClocksAsync(EventId, DistanceId, Arg.Any<CancellationToken>())
            .Returns(new OfficialStartClocks(new DateOnly(2030, 1, 1), new TimeOnly(5, 0), null));
        await h.Sink.RecordAsync([Passage("cp-1.535", 60)]);

        Assert.Equal(OfficialStartUtc.AddSeconds(60), h.Added.Single(log => log.CheckpointId == CpId).OccurredAtUtc);
    }

    [Theory]
    [InlineData(3, true)]
    [InlineData(0, false)]
    public async Task ClearOtherRunsAsync_DeletesOtherRuns_AndNotifiesWhenRowsWereRemoved(int removed, bool notifies)
    {
        var h = Harness();
        h.Logs.DeleteOtherSimulationRunsAsync(EventId, DistanceId, RunId, Arg.Any<CancellationToken>()).Returns(removed);

        await h.Sink.ClearOtherRunsAsync(EventId, DistanceId, RunId);

        await h.Logs.Received(1).DeleteOtherSimulationRunsAsync(EventId, DistanceId, RunId, Arg.Any<CancellationToken>());
        if (!notifies)
        {
            Assert.Empty(h.Published);
            return;
        }

        var notification = Assert.Single(h.Published);
        Assert.Equal(RaceEventLogNotificationKinds.SimulationCleared, notification.Kind);
        Assert.Equal(DistanceId, notification.EventDistanceId);
        Assert.Equal(RunId, notification.SimulationRunId);
    }

    [Fact]
    public void PassageId_IsStablePerRunRunnerAndCheckpoint()
    {
        var id = SimulationTimelineSink.PassageId(RunId, RegistrationId, "cp-0");

        Assert.Equal(id, SimulationTimelineSink.PassageId(RunId, RegistrationId, "cp-0"));
        Assert.NotEqual(id, SimulationTimelineSink.PassageId(Guid.NewGuid(), RegistrationId, "cp-0"));
        Assert.NotEqual(id, SimulationTimelineSink.PassageId(RunId, RegistrationId, "cp-1.535"));
    }

    [Fact]
    public void TryResolveOfficialStart_UsesDistanceStartThenEventStart_InTheConfiguredZone()
    {
        var clocks = new OfficialStartClocks(new DateOnly(2027, 12, 1), null, new TimeOnly(6, 30));

        Assert.True(SimulationTimelineSink.TryResolveOfficialStart("UTC", clocks, out var utc));
        Assert.Equal(new DateTime(2027, 12, 1, 6, 30, 0, DateTimeKind.Utc), utc);
        Assert.Equal(DateTimeKind.Utc, utc.Kind);
        Assert.False(SimulationTimelineSink.TryResolveOfficialStart(null, clocks, out _));
        Assert.False(SimulationTimelineSink.TryResolveOfficialStart("UTC", clocks with { EventStartTime = null }, out _));
    }

    private static HarnessState Harness(bool clocks = true)
    {
        var checkpoints = Substitute.For<IEventCheckpointQuery>();
        checkpoints.ListByEventAsync(EventId, Arg.Any<CancellationToken>()).Returns(
        [
            new EventCheckpointInfo(StartId, EventId, DistanceId, "Xuất phát", "cp-0", "Start", 0),
            new EventCheckpointInfo(CpId, EventId, DistanceId, "CP 0.7km", "cp-1.535", "Water", 1.535)
        ]);
        var starts = Substitute.For<ISimulationOfficialStartSource>();
        starts.GetClocksAsync(EventId, DistanceId, Arg.Any<CancellationToken>())
            .Returns(clocks ? new OfficialStartClocks(new DateOnly(2027, 12, 1), new TimeOnly(6, 0), null) : null);
        var gate = Substitute.For<ISimulationDemoGate>();
        gate.StartTimeZone.Returns("UTC");
        var logs = Substitute.For<IRaceEventLogRepository>();
        var added = new List<RaceEventLog>();
        logs.When(repository => repository.Add(Arg.Any<RaceEventLog>())).Do(call => added.Add(call.Arg<RaceEventLog>()));
        var unitOfWork = Substitute.For<IRaceMonitoringUnitOfWork>();
        var publisher = Substitute.For<IRaceEventLogPublisher>();
        var published = new List<RaceEventLogNotification>();
        publisher.When(target => target.Publish(Arg.Any<RaceEventLogNotification>()))
            .Do(call => published.Add(call.Arg<RaceEventLogNotification>()));
        var sink = new SimulationTimelineSink(
            checkpoints,
            starts,
            gate,
            logs,
            unitOfWork,
            publisher,
            new RaceEventLogChipStarts(checkpoints, logs),
            new SimulationRunAnchorCache(),
            NullLogger<SimulationTimelineSink>.Instance);
        return new HarnessState(sink, logs, unitOfWork, starts, added, published);
    }

    private static HarnessState Harness(OfficialStartClocks? clocks)
    {
        var state = Harness();
        state.Starts.GetClocksAsync(EventId, DistanceId, Arg.Any<CancellationToken>()).Returns(clocks);
        return state;
    }

    private sealed record HarnessState(
        SimulationTimelineSink Sink,
        IRaceEventLogRepository Logs,
        IRaceMonitoringUnitOfWork UnitOfWork,
        ISimulationOfficialStartSource Starts,
        List<RaceEventLog> Added,
        List<RaceEventLogNotification> Published);
}
