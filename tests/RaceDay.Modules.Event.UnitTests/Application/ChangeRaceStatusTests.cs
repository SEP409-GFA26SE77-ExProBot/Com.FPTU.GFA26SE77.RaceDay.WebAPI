using NSubstitute;
using RaceDay.Modules.Event.Application.Abstractions;
using RaceDay.Modules.Event.Application.Certificates;
using RaceDay.Modules.Event.Application.Registrations;
using RaceDay.Modules.Event.Domain.Events;
using RaceDay.Modules.Event.Domain.Registrations;
using RaceDay.Modules.Event.Domain.Staff;
using RaceDay.Shared.Application.Abstractions;
using RaceDay.Shared.Domain.Exceptions;

namespace RaceDay.Modules.Event.UnitTests.Application;

public class ChangeRaceStatusTests
{
    private readonly IRaceEventRepository _events = Substitute.For<IRaceEventRepository>();
    private readonly IEventRegistrationRepository _registrations = Substitute.For<IEventRegistrationRepository>();
    private readonly IEventStaffMemberRepository _staff = Substitute.For<IEventStaffMemberRepository>();
    private readonly IRaceStatusChangeRepository _changes = Substitute.For<IRaceStatusChangeRepository>();
    private readonly IEventUnitOfWork _uow = Substitute.For<IEventUnitOfWork>();
    private readonly ICurrentUserAccessor _user = Substitute.For<ICurrentUserAccessor>();
    private readonly Guid _userId = Guid.NewGuid();
    private readonly RaceEvent _race;
    private readonly EventRegistration _reg;
    private static readonly DateTime Now = new(2026, 11, 3, 8, 0, 0, DateTimeKind.Utc);

    public ChangeRaceStatusTests()
    {
        _race = RaceEvent.CreateDraft(Guid.NewGuid(), "Run", "Hanoi", new(2026, 11, 3),
            new GeoPoint(21, 105), OrganizerInfo.Create("Organizer", "org@test.vn", "0900000000"), 100);
        var distance = EventDistance.Create(_race.Id, "10K", 10, 100, 0);
        typeof(EventDistance).GetProperty(nameof(EventDistance.StartAt))!.SetValue(distance, new TimeOnly(8, 0));
        _race.ReplaceDistances([distance]);
        _reg = EventRegistration.Create(_race.Id, distance.Id, _userId,
            "Runner", "runner@test.vn", "0900000000", null, null, null, null, null,
            null, null, null, null, null, null, "[]", null, null, true);
        _reg.Confirm("1001");
        _events.GetByIdWithDetailsAsync(_race.Id, Arg.Any<CancellationToken>()).Returns(_race);
        _registrations.GetByIdAsync(_reg.Id, Arg.Any<CancellationToken>()).Returns(_reg);
        _user.UserId.Returns(_userId);
    }

    private ChangeRaceStatusCommandHandler Handler() => new(_events, _registrations, _staff, _changes, _uow, _user, new FixedClock());
    private ChangeRaceStatusCommand Command(string status = "dq", string reason = "Rule violation") => new(_race.Id, _reg.Id, status, reason);
    private sealed class FixedClock : TimeProvider { public override DateTimeOffset GetUtcNow() => Now; }

    private EventStaffMember Assign(EventStaffRole role, Guid? eventId = null, Guid? userId = null)
    {
        var member = EventStaffMember.AddDirect(eventId ?? _race.Id, role, "Staff", "staff@test.vn", null, null, null);
        member.LinkUser(userId ?? _userId);
        _staff.ListActiveByUserIdAsync(_userId, Arg.Any<CancellationToken>()).Returns(new[] { member });
        return member;
    }

    [Theory]
    [InlineData(EventStaffRole.Volunteer)]
    [InlineData(EventStaffRole.Coordinator)]
    public async Task ActiveStaff_CanDecide_WithServerAudit(EventStaffRole role)
    {
        Assign(role);
        var dto = await Handler().Handle(Command(), default);
        Assert.Equal("dq", dto.RaceStatus);
        Assert.Equal("not_started", dto.PreviousStatus);
        Assert.Equal(_userId, dto.ChangedByUserId);
        Assert.Equal(Now, dto.ChangedAtUtc);
        await _changes.Received(1).AddAsync(Arg.Is<RaceStatusChange>(x => x.RegistrationId == _reg.Id), default);
        await _uow.Received(1).SaveChangesAsync(default);
    }

    [Theory]
    [InlineData("admin")]
    [InlineData("organizer")]
    public async Task AdminAndOwner_CanDecideWithoutStaffAssignment(string role)
    {
        _user.IsAdmin.Returns(role == "admin");
        if (role == "organizer") _user.UserId.Returns(_race.OrganizerUserId);
        Assert.Equal("dq", (await Handler().Handle(Command(), default)).RaceStatus);
    }

    [Theory]
    [InlineData("photographer")]
    [InlineData("revoked")]
    [InlineData("invited")]
    [InlineData("other_event")]
    [InlineData("other_user")]
    [InlineData("unassigned")]
    public async Task UnauthorizedAssignments_CannotDecide(string scenario)
    {
        if (scenario != "unassigned")
        {
            var member = Assign(scenario == "photographer" ? EventStaffRole.Photographer : EventStaffRole.Volunteer,
                scenario == "other_event" ? Guid.NewGuid() : null, scenario == "other_user" ? Guid.NewGuid() : null);
            if (scenario == "revoked") member.Revoke();
            if (scenario == "invited") typeof(EventStaffMember).GetProperty(nameof(EventStaffMember.Status))!.SetValue(member, EventStaffStatus.Invited);
        }
        await Assert.ThrowsAsync<DomainException>(() => Handler().Handle(Command(), default));
        await _uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RegistrationMustBelongToRouteEvent()
    {
        _user.IsAdmin.Returns(true);
        typeof(EventRegistration).GetProperty(nameof(EventRegistration.EventId))!.SetValue(_reg, Guid.NewGuid());
        await Assert.ThrowsAsync<DomainException>(() => Handler().Handle(Command(), default));
    }

    [Fact]
    public async Task Retry_ReturnsOriginalAudit_WithoutAnotherWrite()
    {
        Assign(EventStaffRole.Volunteer);
        var change = _reg.ChangeRaceStatus(RaceStatus.DQ, "Rule violation", Guid.NewGuid(), Now.AddMinutes(-1), null);
        _changes.GetLatestAsync(_reg.Id, Arg.Any<CancellationToken>()).Returns(change);
        var dto = await Handler().Handle(Command(), default);
        Assert.Equal(change.ChangedByUserId, dto.ChangedByUserId);
        Assert.Equal(change.ChangedAtUtc, dto.ChangedAtUtc);
        await _uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        await Assert.ThrowsAsync<DomainException>(() => Handler().Handle(Command(reason: "Changed reason"), default));
    }

    [Fact]
    public async Task Dns_AtScheduledStart_IsAllowed()
    {
        Assign(EventStaffRole.Volunteer);
        Assert.Equal("dns", (await Handler().Handle(Command("dns", "Absent"), default)).RaceStatus);
    }

    [Fact]
    public async Task DqAfterFinish_RemovesCertificateAndBothLeaderboards()
    {
        Assign(EventStaffRole.Volunteer);
        _reg.Start(Now.AddHours(-1));
        _reg.Finish(Now, 3600);
        _registrations.ListByEventAsync(_race.Id, Arg.Any<CancellationToken>()).Returns(new[] { _reg });
        _registrations.ListByUserAsync(_userId, Arg.Any<CancellationToken>()).Returns(new[] { _reg });
        await Handler().Handle(Command(), default);

        Assert.Null(await new GetRunnerCertificateQueryHandler(_registrations, _events, _user).Handle(new(_reg.Id), default));
        Assert.Empty((await new GetDistanceLeaderboardQueryHandler(_events, _registrations)
            .Handle(new(_race.Id, _reg.DistanceId), default)).Items);
        Assert.Empty(await new GetMyLeaderboardResultsQueryHandler(_user, _registrations, _events).Handle(new(), default));
        Assert.Equal("dq", RegistrationDtoMapper.ToDto(_reg).RaceStatus);
    }

    [Theory]
    [InlineData("finished", "Reason")]
    [InlineData("3", "Reason")]
    [InlineData("dq", "")]
    [InlineData("dq", "   ")]
    [InlineData(null, "Reason")]
    public void Validator_RejectsInvalidRequests(string? status, string reason)
        => Assert.False(new ChangeRaceStatusCommandValidator().Validate(Command(status!, reason)).IsValid);

    [Fact]
    public void Validator_RejectsLongReasonAndEmptyIds()
    {
        var validator = new ChangeRaceStatusCommandValidator();
        Assert.False(validator.Validate(Command(reason: new string('x', 1001))).IsValid);
        Assert.False(validator.Validate(new ChangeRaceStatusCommand(Guid.Empty, Guid.Empty, "dq", "Reason")).IsValid);
    }
}
