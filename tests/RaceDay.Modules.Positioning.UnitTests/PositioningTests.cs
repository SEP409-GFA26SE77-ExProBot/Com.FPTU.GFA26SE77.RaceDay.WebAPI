using NSubstitute;
using RaceDay.Modules.Event.Contracts.Events;
using RaceDay.Modules.Map.Contracts.Courses;
using RaceDay.Modules.Positioning.Application;
using RaceDay.Modules.Positioning.Contracts;
using RaceDay.Modules.Positioning.Domain;
using RaceDay.Modules.Positioning.Infrastructure;
using RaceDay.Shared.Application.Abstractions;
using RaceDay.Shared.Domain.Exceptions;

namespace RaceDay.Modules.Positioning.UnitTests;

public sealed class PositioningTests
{
    private static readonly Guid EventId = Guid.NewGuid();
    private static readonly Guid StaffId = Guid.NewGuid();
    private static readonly Guid DistanceId = Guid.NewGuid();
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-11-14T08:00:00Z");

    [Theory]
    [InlineData(0.0044, "inside")]
    [InlineData(0.005, "outside")]
    [InlineData(0.0045, "uncertain")]
    public async Task Report_CheckpointRadiusAccountsForGpsAccuracy(double deltaLatitude, string expected)
    {
        var fixture = new Fixture([$"map:{DistanceId}:checkpoint"]);
        var result = await fixture.Handle(new ReportPositionBody(deltaLatitude, 0, 5, Now));
        Assert.Equal(expected, result?.ZoneStatus);
        if (expected == "outside") fixture.Alerts.Received(1).Publish(Arg.Any<PositionAlert>());
        else fixture.Alerts.DidNotReceive().Publish(Arg.Any<PositionAlert>());
    }

    [Theory]
    [InlineData(0.0005, "inside")]
    [InlineData(0.0012, "outside")]
    public async Task Report_MobileAreaFollowsCourseSegments(double latitude, string expected)
    {
        var fixture = new Fixture(["duty:float"]);
        var result = await fixture.Handle(new ReportPositionBody(latitude, 0.005, 3, Now));
        Assert.Equal(expected, result?.ZoneStatus);
    }

    [Fact]
    public async Task Report_RejectsUnknownStaffAndInvalidSamples()
    {
        var fixture = new Fixture([]);
        Assert.Null(await fixture.Handle(new ReportPositionBody(0, 0, 5, Now), Guid.NewGuid()));
        await Assert.ThrowsAsync<DomainException>(() => fixture.Handle(new ReportPositionBody(91, 0, 1, Now)));
        await Assert.ThrowsAsync<DomainException>(() => fixture.Handle(new ReportPositionBody(0, 0, 1, Now.AddMinutes(-3))));
        Assert.Equal("unconfigured", (await fixture.Handle(new ReportPositionBody(0, 0, 5, Now)))?.ZoneStatus);
    }

    [Fact]
    public async Task Tracker_AlertsOncePerExitAndIgnoresStaleSamples()
    {
        using var tracker = new CachedPositionTracker(new MemoryCache());
        Task<bool> Send(int offset, bool? outside) => tracker.RecordAsync(EventId, StaffId,
            new PositionSample(new GeoPoint(0, 0), 1, Now.AddSeconds(offset)), outside, CancellationToken.None);

        Assert.False(await Send(0, false));
        Assert.True(await Send(5, true));
        Assert.False(await Send(10, true));
        Assert.False(await Send(15, null));
        Assert.False(await Send(12, false));
        Assert.False(await Send(20, true));
        Assert.False(await Send(25, false));
        Assert.True(await Send(30, true));
    }

    [Fact]
    public async Task Stream_OnlyReceivesAlertsFromSubscribedEvent()
    {
        var stream = new PositionAlertStream();
        await using var listener = stream.ListenAsync(EventId, CancellationToken.None).GetAsyncEnumerator();
        Assert.True(await listener.MoveNextAsync());
        Assert.Null(listener.Current);
        var alert = new PositionAlert(EventId, Guid.NewGuid(), "Staff", "race-staff", Now);
        stream.Publish(alert with { EventId = Guid.NewGuid() });
        stream.Publish(alert);
        Assert.True(await listener.MoveNextAsync());
        Assert.Equal(alert, listener.Current);
    }

    private sealed class Fixture
    {
        public IPositionAlertPublisher Alerts { get; } = Substitute.For<IPositionAlertPublisher>();
        private readonly ReportPositionCommandHandler _handler;

        public Fixture(string[] stations)
        {
            var staff = Substitute.For<IEventPositioningAccessQuery>();
            staff.ListActiveAsync(EventId, Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(
                call => call.ArgAt<Guid>(1) == StaffId
                    ? new[] { new PositioningStaffAssignment(StaffId, "Staff", "photographer", stations) }
                    : Array.Empty<PositioningStaffAssignment>());
            var organizers = Substitute.For<IEventOrganizerAccessQuery>();
            var courses = Substitute.For<IEventCoursesReadQuery>();
            courses.ListAsync(EventId, Arg.Any<CancellationToken>()).Returns(new[]
            {
                new RaceCourseSnapshot(EventId, DistanceId, "Course", 5,
                    [new RaceCoursePoint(0, 0), new RaceCoursePoint(0, 0.01), new RaceCoursePoint(0.01, 0.01)],
                    [new RaceCourseCheckpoint("checkpoint", "Checkpoint", 1, 0, 0, "cp")])
            });
            var tracker = Substitute.For<IPositionTracker>();
            tracker.RecordAsync(EventId, StaffId, Arg.Any<PositionSample>(), true, Arg.Any<CancellationToken>()).Returns(true);
            _handler = new ReportPositionCommandHandler(new PositioningAccess(organizers, staff), courses, tracker, Alerts, new FixedClock());
        }

        public Task<PositionReceipt?> Handle(ReportPositionBody body, Guid? userId = null)
            => _handler.Handle(new ReportPositionCommand(EventId, userId ?? StaffId, body), CancellationToken.None);
    }

    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class MemoryCache : ICacheService
    {
        private readonly Dictionary<string, object?> _values = new();

        public Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default)
            => Task.FromResult(_values.TryGetValue(key, out var value) ? (T?)value : default);

        public Task SetAsync<T>(string key, T value, TimeSpan? expiry = null, CancellationToken cancellationToken = default)
        {
            _values[key] = value;
            return Task.CompletedTask;
        }

        public Task RemoveAsync(string key, CancellationToken cancellationToken = default)
        {
            _values.Remove(key);
            return Task.CompletedTask;
        }
    }
}
