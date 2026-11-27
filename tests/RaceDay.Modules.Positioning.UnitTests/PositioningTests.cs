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
    private static readonly Guid OrganizerId = Guid.NewGuid();
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

    [Theory]
    [InlineData(61, false)]
    [InlineData(60, true)]
    [InlineData(-1, true)]
    [InlineData(null, false)]
    public async Task Report_TracksRegardlessOfWhenAlertsStart(int? minutesUntilStart, bool alertsEnabled)
    {
        var fixture = new Fixture(["duty:float"], minutesUntilStart);
        var result = await fixture.Handle(new ReportPositionBody(0.005, 0, 3, Now));

        Assert.Equal("outside", result?.ZoneStatus);
        await fixture.Tracker.Received(1).RecordAsync(EventId, StaffId, Arg.Any<PositionSample>(),
            alertsEnabled ? true : null, Arg.Any<CancellationToken>());
        fixture.Alerts.Received(1).Publish(EventId, Arg.Is<LiveStaffPosition>(position => position.UserId == StaffId));
        if (alertsEnabled) fixture.Alerts.Received(1).Publish(Arg.Any<PositionAlert>());
        else fixture.Alerts.DidNotReceive().Publish(Arg.Any<PositionAlert>());
    }

    [Fact]
    public async Task ListLivePositions_ReturnsBothRolesAndOnlyFreshSamplesToEventMembers()
    {
        var fixture = new Fixture(["duty:float"], minutesUntilStart: 300);
        var otherStaffId = Guid.NewGuid();
        var staleId = Guid.NewGuid();
        var staff = new[]
        {
            new PositioningStaffAssignment(Guid.NewGuid(), StaffId, "Photo", "photographer", [new("duty:float", null)]),
            new PositioningStaffAssignment(Guid.NewGuid(), otherStaffId, "Staff", "race-staff", [new($"map:{DistanceId}:cp-2", "Checkpoint 2")]),
            new PositioningStaffAssignment(Guid.NewGuid(), staleId, "Offline", "race-staff", [])
        };
        fixture.Staff.ListActiveAsync(EventId, null, Arg.Any<CancellationToken>()).Returns(staff);
        foreach (var member in staff)
        {
            var capturedAt = member.UserId == staleId ? Now.AddSeconds(-21) : Now;
            fixture.Tracker.GetLatestAsync(EventId, member.UserId, Arg.Any<CancellationToken>())
                .Returns(new PositionSample(new GeoPoint(21, 105), 3, capturedAt));
        }

        var query = new ListLiveStaffPositionsQueryHandler(fixture.Access, fixture.Staff, fixture.Tracker, new FixedClock());
        Assert.Null(await query.Handle(new(EventId, Guid.NewGuid(), 0), CancellationToken.None));
        var result = await query.Handle(new(EventId, StaffId, 0), CancellationToken.None);
        Assert.NotNull(result);
        Assert.Null(result.NextOffset);
        Assert.Equal(2, result.Items.Count);
        Assert.Contains(result.Items, item => item.UserId == StaffId && item.Roles.Single() == "photographer" && item.Latitude == 21);
        Assert.Contains(result.Items, item => item.UserId == otherStaffId && item.Stations.Single().Label == "Checkpoint 2");
    }

    [Fact]
    public async Task Tracker_AlertsOncePerExitAndIgnoresStaleSamples()
    {
        using var tracker = new CachedPositionTracker(new MemoryCache());
        async Task<bool> Send(int offset, bool? outside) => (await tracker.RecordAsync(EventId, StaffId,
            new PositionSample(new GeoPoint(0, 0), 1, Now.AddSeconds(offset)), outside, CancellationToken.None)).BecameOutside;
        Task<PositionRecordResult> Record(int offset, bool? outside) => tracker.RecordAsync(EventId, StaffId,
            new PositionSample(new GeoPoint(0, 0), 1, Now.AddSeconds(offset)), outside, CancellationToken.None);

        Assert.False(await Send(0, false));
        Assert.True(await Send(5, true));
        Assert.False(await Send(10, true));
        Assert.False(await Send(15, null));
        Assert.False((await Record(12, false)).Accepted);
        Assert.False(await Send(20, true));
        Assert.False(await Send(25, false));
        Assert.True(await Send(30, true));
        var latest = await tracker.GetLatestAsync(EventId, StaffId, CancellationToken.None);
        Assert.Equal(Now.AddSeconds(30), latest?.CapturedAtUtc);
    }

    [Fact]
    public async Task Stream_SendsAlertsOnlyToTheStaffWhoLeftTheZone()
    {
        var stream = new PositionEventStream();
        var otherStaffId = Guid.NewGuid();
        await using var listener = stream.ListenAsync(EventId, StaffId, includePositions: false, CancellationToken.None).GetAsyncEnumerator();
        await using var live = stream.ListenAsync(EventId, otherStaffId, includePositions: true, CancellationToken.None).GetAsyncEnumerator();
        Assert.True(await listener.MoveNextAsync());
        Assert.True(await live.MoveNextAsync());
        Assert.Null(listener.Current);
        var alert = new PositionAlert(EventId, StaffId, Guid.NewGuid(), "Staff", "race-staff", Now);
        var otherAlert = alert with { UserId = otherStaffId };
        var position = new LiveStaffPosition(StaffId, "Staff", ["race-staff"], [], 21, 105, Now);
        stream.Publish(Guid.NewGuid(), position);
        stream.Publish(EventId, position);
        stream.Publish(alert with { EventId = Guid.NewGuid() });
        stream.Publish(alert);
        stream.Publish(otherAlert);

        Assert.True(await listener.MoveNextAsync());
        Assert.Equal(alert, listener.Current?.Alert);
        Assert.True(await live.MoveNextAsync());
        Assert.Equal(position, live.Current?.Position);
        // The live map viewer does not get StaffId's alert, only its own.
        Assert.True(await live.MoveNextAsync());
        Assert.Equal(otherAlert, live.Current?.Alert);
    }

    [Fact]
    public async Task Report_RefusesStaffWhoHaveNotCheckedIn()
    {
        var fixture = new Fixture(["duty:float"]);
        fixture.CheckIns.GetAsync(EventId, StaffId, Arg.Any<CancellationToken>()).Returns((DateTimeOffset?)null);

        var result = await fixture.Handle(new ReportPositionBody(0.005, 0, 3, Now));

        Assert.Equal(PositionReceipt.NotCheckedIn, result);
        await fixture.Tracker.DidNotReceiveWithAnyArgs().RecordAsync(default, default, default!, default, default);
        fixture.Alerts.DidNotReceiveWithAnyArgs().Publish(default!);
    }

    [Fact]
    public async Task Report_AlertCarriesTheReportingUser()
    {
        var fixture = new Fixture(["duty:float"]);
        await fixture.Handle(new ReportPositionBody(0.005, 0, 3, Now));
        fixture.Alerts.Received(1).Publish(Arg.Is<PositionAlert>(alert => alert.UserId == StaffId && alert.EventId == EventId));
    }

    [Fact]
    public void Codes_RotateEveryMinuteWithShortGrace()
    {
        var codes = new StaffCheckInCodes(new byte[32]);
        var issued = codes.Issue(EventId, Now.AddSeconds(30));

        Assert.Equal(Now, issued.IssuedAtUtc);
        Assert.Equal(Now.AddSeconds(60), issued.ExpiresAtUtc);
        Assert.True(codes.IsValid(EventId, issued.Payload, Now.AddSeconds(59)));
        Assert.True(codes.IsValid(EventId, $"  {issued.Payload}\n", Now));
        Assert.True(codes.IsValid(EventId, issued.Payload, Now.AddSeconds(60 + StaffCheckInCodes.GraceSeconds - 1)));
        Assert.False(codes.IsValid(EventId, issued.Payload, Now.AddSeconds(60 + StaffCheckInCodes.GraceSeconds)));
        Assert.False(codes.IsValid(EventId, issued.Payload, Now.AddSeconds(-1)));
        Assert.False(codes.IsValid(Guid.NewGuid(), issued.Payload, Now));
        Assert.False(new StaffCheckInCodes(new byte[] { 1 }).IsValid(EventId, issued.Payload, Now));
        var tampered = issued.Payload[..^1] + (issued.Payload[^1] == '0' ? '1' : '0');
        Assert.False(codes.IsValid(EventId, tampered, Now));
        Assert.False(codes.IsValid(EventId, null, Now));
        Assert.NotEqual(issued.Payload, codes.Issue(EventId, Now.AddSeconds(60)).Payload);
    }

    [Fact]
    public async Task CheckIn_AcceptsCurrentCodeAndRejectsWrongOne()
    {
        var fixture = new Fixture(["duty:float"]);
        var codes = new StaffCheckInCodes(new byte[32]);
        var handler = new StaffCheckInCommandHandler(fixture.Access, codes, fixture.CheckIns, new FixedClock());
        var current = codes.Issue(EventId, Now).Payload;

        var error = await Assert.ThrowsAsync<DomainException>(() =>
            handler.Handle(new StaffCheckInCommand(EventId, StaffId, "raceday-staff-checkin:wrong"), CancellationToken.None));
        Assert.Equal(StaffCheckInRules.WrongCode, error.Message);
        await fixture.CheckIns.DidNotReceiveWithAnyArgs().SetAsync(default, default, default, default);

        Assert.Null(await handler.Handle(new StaffCheckInCommand(EventId, Guid.NewGuid(), current), CancellationToken.None));
        var receipt = await handler.Handle(new StaffCheckInCommand(EventId, StaffId, current), CancellationToken.None);
        Assert.Equal(Now, receipt?.CheckedInAtUtc);
        await fixture.CheckIns.Received(1).SetAsync(EventId, StaffId, Now, Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(61, false)]
    [InlineData(60, true)]
    [InlineData(null, false)]
    public async Task CheckIn_OpensOneHourBeforeStart(int? minutesUntilStart, bool open)
    {
        var fixture = new Fixture(["duty:float"], minutesUntilStart);
        var codes = new StaffCheckInCodes(new byte[32]);
        var handler = new StaffCheckInCommandHandler(fixture.Access, codes, fixture.CheckIns, new FixedClock());
        var command = new StaffCheckInCommand(EventId, StaffId, codes.Issue(EventId, Now).Payload);

        if (open) Assert.NotNull(await handler.Handle(command, CancellationToken.None));
        else await Assert.ThrowsAsync<DomainException>(() => handler.Handle(command, CancellationToken.None));
    }

    [Fact]
    public async Task IssueCode_OnlyForTheOrganizer()
    {
        var fixture = new Fixture(["duty:float"]);
        var handler = new IssueStaffCheckInCodeQueryHandler(fixture.Access, new StaffCheckInCodes(new byte[32]), new FixedClock());

        Assert.Null(await handler.Handle(new IssueStaffCheckInCodeQuery(EventId, StaffId), CancellationToken.None));
        var code = await handler.Handle(new IssueStaffCheckInCodeQuery(EventId, OrganizerId), CancellationToken.None);
        Assert.NotNull(code);
        Assert.StartsWith("raceday-staff-checkin:", code.Payload);
    }

    private sealed class Fixture
    {
        public IPositionEventPublisher Alerts { get; } = Substitute.For<IPositionEventPublisher>();
        public IPositionTracker Tracker { get; } = Substitute.For<IPositionTracker>();
        public IEventPositioningAccessQuery Staff { get; } = Substitute.For<IEventPositioningAccessQuery>();
        public IStaffCheckInStore CheckIns { get; } = Substitute.For<IStaffCheckInStore>();
        public PositioningAccess Access { get; }
        private readonly ReportPositionCommandHandler _handler;

        public Fixture(string[] stations, int? minutesUntilStart = 60)
        {
            Staff.ListActiveAsync(EventId, Arg.Any<Guid?>(), Arg.Any<CancellationToken>()).Returns(
                call => call.ArgAt<Guid?>(1) == StaffId
                    ? new[] { new PositioningStaffAssignment(Guid.NewGuid(), StaffId, "Staff", "photographer",
                        stations.Select(id => new PositioningStaffStation(id, null)).ToArray()) }
                    : Array.Empty<PositioningStaffAssignment>());
            var organizers = Substitute.For<IEventOrganizerAccessQuery>();
            organizers.GetAsync(EventId, Arg.Any<CancellationToken>()).Returns(
                new EventOrganizerAccessInfo(EventId, OrganizerId, "Approved", "Race",
                    minutesUntilStart is { } minutes ? Now.AddMinutes(minutes) : null));
            Access = new PositioningAccess(organizers, Staff);
            var courses = Substitute.For<IEventCoursesReadQuery>();
            courses.ListAsync(EventId, Arg.Any<CancellationToken>()).Returns(new[]
            {
                new RaceCourseSnapshot(EventId, DistanceId, "Course", 5,
                    [new RaceCoursePoint(0, 0), new RaceCoursePoint(0, 0.01), new RaceCoursePoint(0.01, 0.01)],
                    [new RaceCourseCheckpoint("checkpoint", "Checkpoint", 1, 0, 0, "cp")])
            });
            Tracker.RecordAsync(EventId, StaffId, Arg.Any<PositionSample>(), Arg.Any<bool?>(), Arg.Any<CancellationToken>())
                .Returns(call => new PositionRecordResult(true, call.Arg<bool?>() == true));
            CheckIns.GetAsync(EventId, Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(Now);
            _handler = new ReportPositionCommandHandler(Access, courses, Tracker, Alerts, CheckIns, new FixedClock());
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
