using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using RaceDay.Modules.Event.Contracts.Events;
using RaceDay.Modules.RaceMonitoring.Application.Abstractions;
using RaceDay.Modules.RaceMonitoring.Application.CutoffWarnings;
using RaceDay.Modules.RaceMonitoring.Domain;
using RaceDay.Modules.RaceMonitoring.Domain.RunnerMonitoring;

namespace RaceDay.Modules.RaceMonitoring.UnitTests.Application;

public class DetectCutoffWarningsHandlerTests
{
    private static readonly TimeZoneInfo Vietnam =
        TimeZoneInfo.CreateCustomTimeZone("test+07", TimeSpan.FromHours(7), "test+07", "test+07");

    private static readonly Guid EventId = Guid.NewGuid();
    private static readonly Guid DistanceId = Guid.NewGuid();
    private static readonly Guid StartId = Guid.NewGuid();
    private static readonly Guid Cp1Id = Guid.NewGuid();
    private static readonly Guid Cp2Id = Guid.NewGuid();
    private static readonly Guid RunnerId = Guid.NewGuid();

    // CP1 closes 07:00 local = 00:00 UTC. CP2 closes 08:00 local.
    private static readonly DateTime Cp1CutoffUtc = new(2026, 11, 23, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime StartedAtUtc = Cp1CutoffUtc.AddHours(-1);
    private static readonly DateTime Now = Cp1CutoffUtc.AddMinutes(5);

    [Fact]
    public async Task StartedRunner_MissingCheckpointAfterGrace_RaisesWarningAndNotifies()
    {
        var h = new Harness();
        h.Passages.Add(new CheckpointFirstPassage(RunnerId, StartId, StartedAtUtc));

        var result = await h.RunAsync();

        Assert.Equal(1, result.Raised);
        var warning = Assert.Single(h.Added);
        Assert.Equal(Cp1Id, warning.CheckpointId);
        Assert.Equal(Cp1CutoffUtc, warning.CutoffAtUtc);
        Assert.Equal(StartId, warning.LastCheckpointId);
        Assert.Equal("Start", warning.LastCheckpointName);
        Assert.Equal(StartedAtUtc, warning.LastPassedAtUtc);
        Assert.Equal(Now, warning.DetectedAtUtc);
        var notification = Assert.Single(h.Published);
        Assert.Equal(CutoffWarningNotificationKind.Raised, notification.Kind);
        Assert.Equal("1024", notification.BibNumber);
        await h.UnitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task WithinGracePeriod_DoesNotRaise()
    {
        var h = new Harness { NowUtc = Cp1CutoffUtc.AddSeconds(90) };
        h.Passages.Add(new CheckpointFirstPassage(RunnerId, StartId, StartedAtUtc));

        var result = await h.RunAsync();

        Assert.Equal(0, result.Raised);
        Assert.Empty(h.Added);
        await h.UnitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PassedCheckpointLate_DoesNotRaise()
    {
        var h = new Harness();
        h.Passages.Add(new CheckpointFirstPassage(RunnerId, Cp1Id, Cp1CutoffUtc.AddMinutes(2)));

        await h.RunAsync();

        Assert.Empty(h.Added);
    }

    [Fact]
    public async Task PassedLaterCheckpoint_TreatsMissingReadAsPassed()
    {
        var h = new Harness { NowUtc = Cp1CutoffUtc.AddHours(1).AddMinutes(5) };
        h.Passages.Add(new CheckpointFirstPassage(RunnerId, StartId, StartedAtUtc));
        h.Passages.Add(new CheckpointFirstPassage(RunnerId, Cp2Id, Cp1CutoffUtc.AddMinutes(40)));

        await h.RunAsync();

        Assert.Empty(h.Added);
    }

    [Fact]
    public async Task ExistingWarningForSameRunnerAndCheckpoint_IsNotDuplicated()
    {
        var h = new Harness();
        h.Passages.Add(new CheckpointFirstPassage(RunnerId, StartId, StartedAtUtc));
        h.Keys.Add(new CutoffWarningKey(RunnerId, Cp1Id));

        await h.RunAsync();

        Assert.Empty(h.Added);
    }

    [Fact]
    public async Task MissedTwoCheckpoints_RaisesOneWarningPerCheckpoint()
    {
        var h = new Harness { NowUtc = Cp1CutoffUtc.AddHours(1).AddMinutes(5) };
        h.Passages.Add(new CheckpointFirstPassage(RunnerId, StartId, StartedAtUtc));

        await h.RunAsync();

        Assert.Equal(2, h.Added.Count);
        Assert.Equal(new HashSet<Guid> { Cp1Id, Cp2Id }, h.Added.Select(w => w.CheckpointId).ToHashSet());
    }

    [Fact]
    public async Task NoStartEvidence_SkipsRunner()
    {
        var h = new Harness();
        h.Runners[0] = h.Runners[0] with { StartedAtUtc = null, RaceStatus = "NotStarted" };

        await h.RunAsync();

        Assert.Empty(h.Added);
    }

    [Fact]
    public async Task NoStartEvidence_RaisesWhenStartEvidenceIsNotRequired()
    {
        var h = new Harness { RequireStartEvidence = false };
        h.Runners[0] = h.Runners[0] with { StartedAtUtc = null, RaceStatus = "NotStarted" };

        await h.RunAsync();

        var warning = Assert.Single(h.Added);
        Assert.Null(warning.LastCheckpointId);
    }

    [Theory]
    [InlineData("DNF")]
    [InlineData("DNS")]
    [InlineData("Finished")]
    public async Task OffCourseRunner_DoesNotRaise(string raceStatus)
    {
        var h = new Harness();
        h.Runners[0] = h.Runners[0] with { RaceStatus = raceStatus };

        await h.RunAsync();

        Assert.Empty(h.Added);
    }

    [Fact]
    public async Task ActiveWarning_ResolvedWhenDelayedPassageArrives()
    {
        var h = new Harness();
        var active = h.ActiveWarning();
        h.Passages.Add(new CheckpointFirstPassage(RunnerId, Cp1Id, Cp1CutoffUtc.AddSeconds(-30)));

        var result = await h.RunAsync();

        Assert.Equal(1, result.Resolved);
        Assert.Equal(CutoffWarningStatus.Resolved, active.Status);
        Assert.Equal(CutoffWarningResolution.PassedCheckpoint, active.Resolution);
        Assert.Equal(CutoffWarningNotificationKind.Resolved, Assert.Single(h.Published).Kind);
    }

    [Fact]
    public async Task ActiveWarning_ResolvedWhenRunnerPassesLaterCheckpoint()
    {
        var h = new Harness();
        var active = h.ActiveWarning();
        h.Passages.Add(new CheckpointFirstPassage(RunnerId, Cp2Id, Cp1CutoffUtc.AddMinutes(4)));

        await h.RunAsync();

        Assert.Equal(CutoffWarningResolution.PassedLaterCheckpoint, active.Resolution);
    }

    [Fact]
    public async Task ActiveWarning_ResolvedWhenRunnerMarkedDnf()
    {
        var h = new Harness();
        var active = h.ActiveWarning();
        h.Runners[0] = h.Runners[0] with { RaceStatus = "DNF" };

        await h.RunAsync();

        Assert.Equal(CutoffWarningResolution.RunnerStatusChanged, active.Resolution);
    }

    [Fact]
    public async Task ActiveWarning_StaysActiveWithoutPassage()
    {
        var h = new Harness();
        var active = h.ActiveWarning();
        h.Keys.Add(new CutoffWarningKey(RunnerId, Cp1Id));

        var result = await h.RunAsync();

        Assert.True(active.IsActive);
        Assert.Equal(0, result.Resolved);
        Assert.Empty(h.Published);
    }

    private sealed class Harness
    {
        public IEventCutoffScheduleQuery Schedule { get; } = Substitute.For<IEventCutoffScheduleQuery>();
        public ICheckpointScanRepository Scans { get; } = Substitute.For<ICheckpointScanRepository>();
        public ICutoffWarningRepository Warnings { get; } = Substitute.For<ICutoffWarningRepository>();
        public IRaceMonitoringUnitOfWork UnitOfWork { get; } = Substitute.For<IRaceMonitoringUnitOfWork>();
        public ICutoffWarningPublisher Publisher { get; } = Substitute.For<ICutoffWarningPublisher>();
        public List<EventRaceRunner> Runners { get; } =
            [new(RunnerId, DistanceId, "1024", "Nguyen Van An", "Running", StartedAtUtc)];
        public List<CheckpointFirstPassage> Passages { get; } = [];
        public List<CutoffWarningKey> Keys { get; } = [];
        public List<CutoffWarning> Active { get; } = [];
        public List<CutoffWarning> Added { get; } = [];
        public List<CutoffWarningNotification> Published { get; } = [];
        public DateTime NowUtc { get; init; } = Now;
        public bool RequireStartEvidence { get; init; } = true;

        public CutoffWarning ActiveWarning()
        {
            var warning = CutoffWarning.Raise(
                new CutoffWarningTarget(
                    EventId, "Hanoi Marathon", DistanceId, "21KM", RunnerId, "1024", "Nguyen Van An",
                    Cp1Id, "CP1", 5, 1),
                Cp1CutoffUtc,
                null,
                Cp1CutoffUtc.AddMinutes(2));
            Active.Add(warning);
            return warning;
        }

        public Task<CutoffDetectionResult> RunAsync()
        {
            var schedule = new EventCutoffSchedule(
                EventId,
                "Hanoi Marathon",
                new DateOnly(2026, 11, 23),
                [
                    new EventCutoffDistance(
                        DistanceId,
                        "21KM",
                        new TimeOnly(6, 0),
                        [
                            new EventCutoffCheckpoint(StartId, "Start", 0, 0, "Start", null),
                            new EventCutoffCheckpoint(Cp1Id, "CP1", 5, 1, "Cp", "07:00"),
                            new EventCutoffCheckpoint(Cp2Id, "CP2", 10, 2, "Cp", "08:00")
                        ])
                ]);
            Schedule.ListRaceDayEventsAsync(Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
                .Returns([schedule]);
            Schedule.ListRaceRunnersAsync(EventId, Arg.Any<CancellationToken>()).Returns(_ => Runners.ToArray());
            Scans.ListFirstPassagesByCheckpointsAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
                .Returns(_ => Passages.ToArray());
            Warnings.ListActiveByEventAsync(EventId, Arg.Any<CancellationToken>()).Returns(_ => Active.ToArray());
            Warnings.ListKeysByEventAsync(EventId, Arg.Any<CancellationToken>()).Returns(_ => Keys.ToArray());
            Warnings.When(w => w.Add(Arg.Any<CutoffWarning>())).Do(call => Added.Add(call.Arg<CutoffWarning>()));
            Publisher.When(p => p.Publish(Arg.Any<CutoffWarningNotification>()))
                .Do(call => Published.Add(call.Arg<CutoffWarningNotification>()));

            var handler = new DetectCutoffWarningsCommandHandler(
                Schedule,
                Scans,
                Warnings,
                UnitOfWork,
                Publisher,
                NullLogger<DetectCutoffWarningsCommandHandler>.Instance);
            var settings = new CutoffMonitoringSettings(
                TimeSpan.FromMinutes(2),
                TimeSpan.FromHours(12),
                RequireStartEvidence,
                Vietnam);
            return handler.Handle(new DetectCutoffWarningsCommand(NowUtc, settings), CancellationToken.None);
        }
    }
}
