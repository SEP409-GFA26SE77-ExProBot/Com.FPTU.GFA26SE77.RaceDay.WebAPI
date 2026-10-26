using NSubstitute;
using RaceDay.Modules.Event.Application.Abstractions;
using RaceDay.Modules.Event.Application.Registrations;
using RaceDay.Modules.Event.Domain.Events;
using RaceDay.Modules.Event.Domain.Registrations;
using RaceDay.Modules.Event.Domain.Staff;
using RaceDay.Modules.Event.UnitTests.Domain;
using RaceDay.Shared.Application.Abstractions;
using RaceDay.Shared.Domain.Exceptions;

namespace RaceDay.Modules.Event.UnitTests.Application;

public class RunnerOperationsHandlerTests
{
    private readonly IRaceEventRepository events = Substitute.For<IRaceEventRepository>();
    private readonly IEventRegistrationRepository registrations = Substitute.For<IEventRegistrationRepository>();
    private readonly IEventStaffMemberRepository staff = Substitute.For<IEventStaffMemberRepository>();
    private readonly ICurrentUserAccessor current = Substitute.For<ICurrentUserAccessor>();
    private readonly IEventUnitOfWork uow = Substitute.For<IEventUnitOfWork>();
    private readonly Guid actor = Guid.NewGuid();
    private readonly RaceEvent race;
    private readonly EventRegistration reg;
    private readonly List<RunnerOperation> receipts = [];

    public RunnerOperationsHandlerTests()
    {
        race = RaceEvent.CreateDraft(Guid.NewGuid(), "Test run", "HCM", new DateOnly(2026, 11, 29),
            new GeoPoint(10, 106), OrganizerInfo.Create("Organizer", "org@example.com", "0900000000"), 500);
        reg = RunnerOperationsTests.Registration(race.Id);
        current.UserId.Returns(actor);
        events.GetByIdAsync(race.Id, Arg.Any<CancellationToken>()).Returns(race);
        registrations.GetByIdAsync(reg.Id, Arg.Any<CancellationToken>()).Returns(reg);
        registrations.GetOperationAsync(reg.Id, Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(call => receipts.FirstOrDefault(x => x.RequestId == call.ArgAt<Guid>(1)));
        registrations.AddOperationAsync(Arg.Any<RunnerOperation>(), Arg.Any<CancellationToken>())
            .Returns(call => { receipts.Add(call.Arg<RunnerOperation>()); return Task.CompletedTask; });
        Grant(EventStaffRole.Volunteer);
    }

    private EventStaffMember Grant(EventStaffRole role, Guid? eventId = null, bool active = true, bool linked = true)
    {
        var member = EventStaffMember.Invite(eventId ?? race.Id, role, "staff@example.com", "Staff", null, null, null);
        if (linked) member.LinkUser(actor);
        if (active) member.Activate();
        staff.ListActiveByUserIdAsync(actor, Arg.Any<CancellationToken>()).Returns(new[] { member });
        return member;
    }

    private UpdateRunnerStatusCommand Command(string status = "dnf", string kind = "race_status") =>
        new(race.Id, reg.Id, kind, new(status, "Staff verified", reg.Version, Guid.NewGuid()));

    private Task<RunnerOperationDto> Handle(UpdateRunnerStatusCommand command) =>
        new UpdateRunnerStatusCommandHandler(events, registrations, staff, current, uow).Handle(command, default);

    [Theory]
    [InlineData(EventStaffRole.Volunteer, "dns")]
    [InlineData(EventStaffRole.Volunteer, "dnf")]
    [InlineData(EventStaffRole.Volunteer, "dq")]
    [InlineData(EventStaffRole.Coordinator, "dns")]
    [InlineData(EventStaffRole.Coordinator, "dnf")]
    [InlineData(EventStaffRole.Coordinator, "dq")]
    public async Task BothOperationalRolesCanSetAllThreeStatuses(EventStaffRole role, string status)
    {
        Grant(role);
        var before = reg.Version;
        var result = await Handle(Command(status));
        Assert.Equal(status, result.RaceStatus);
        Assert.Equal(before + 1, result.Version);
        Assert.Equal(actor, result.ActorUserId);
        Assert.Equal("not_started", result.PreviousStatus);
        Assert.False(result.AlreadyApplied);
        Assert.Single(receipts);
        await uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("photographer")]
    [InlineData("other_event")]
    [InlineData("invited")]
    [InlineData("unlinked")]
    [InlineData("revoked")]
    public async Task UnauthorizedMembershipCannotWriteOrReadHistory(string scenario)
    {
        switch (scenario)
        {
            case "photographer": Grant(EventStaffRole.Photographer); break;
            case "other_event": Grant(EventStaffRole.Coordinator, Guid.NewGuid()); break;
            case "invited": Grant(EventStaffRole.Volunteer, active: false); break;
            case "unlinked": Grant(EventStaffRole.Coordinator, linked: false); break;
            case "revoked": Grant(EventStaffRole.Volunteer).Revoke(); break;
        }
        await Assert.ThrowsAsync<ForbiddenException>(() => Handle(Command()));
        await Assert.ThrowsAsync<ForbiddenException>(() => Handle(Command("lost", "bib_status")));
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            new GetRunnerOperationsQueryHandler(events, registrations, staff, current)
                .Handle(new(race.Id, reg.Id), default));
        await uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RegistrationFromAnotherEventIsRejected()
    {
        registrations.GetByIdAsync(reg.Id, Arg.Any<CancellationToken>()).Returns(RunnerOperationsTests.Registration());
        await Assert.ThrowsAsync<NotFoundException>(() => Handle(Command()));
        Assert.Empty(receipts);
    }

    [Fact]
    public async Task RetryReturnsOriginalReceipt_EvenAfterLaterOperations()
    {
        var command = Command();
        var original = await Handle(command);
        await Handle(Command("lost", "bib_status"));
        var retried = await Handle(command);
        Assert.Equal(original.Id, retried.Id);
        Assert.Equal(original.Version, retried.Version);
        Assert.Equal("normal", retried.BibStatus); // receipt is not a current-state snapshot
        Assert.True(retried.AlreadyApplied);
        Assert.Equal(2, receipts.Count);
        await uow.Received(2).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("reason")]
    [InlineData("status")]
    [InlineData("version")]
    [InlineData("kind")]
    [InlineData("actor")]
    public async Task ReusingRequestIdWithDifferentPayloadOrActorIsConflict(string field)
    {
        var command = Command();
        await Handle(command);
        var changed = field switch
        {
            "reason" => command with { Body = command.Body with { Reason = "Different" } },
            "status" => command with { Body = command.Body with { Status = "dq" } },
            "version" => command with { Body = command.Body with { ExpectedVersion = reg.Version } },
            "kind" => command with { Kind = "bib_status", Body = command.Body with { Status = "lost" } },
            _ => command
        };
        if (field == "actor") current.UserId.Returns(race.OrganizerUserId);
        await Assert.ThrowsAsync<ConflictException>(() => Handle(changed));
        Assert.Single(receipts);
    }

    [Fact]
    public async Task StaleVersionAfterScanIsConflictAndCreatesNoAudit()
    {
        var command = Command();
        reg.CheckIn(actor);
        await Assert.ThrowsAsync<ConflictException>(() => Handle(command));
        Assert.Empty(receipts);
        await uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task BibLifecyclePreservesRaceStatus_AndReceiptsTrackEachLoss()
    {
        await Handle(Command("lost", "bib_status"));
        await Handle(Command("reissued", "bib_status"));
        await Handle(Command("lost", "bib_status"));
        Assert.Equal(3, receipts.Count);
        Assert.All(receipts, x => Assert.Equal("TEST-1001", x.BibNumber));
        Assert.Equal(RunnerRaceStatus.NotStarted, reg.RaceStatus);
        Assert.Null(reg.CheckedInAtUtc);
    }

    [Fact]
    public async Task RestoreRequiresCoordinatorOrOwner_AndKeepsAudit()
    {
        reg.Start(DateTime.UtcNow);
        await Handle(Command());
        await Assert.ThrowsAsync<ForbiddenException>(() => Handle(Command("restore")));
        Grant(EventStaffRole.Coordinator);
        var restored = await Handle(Command("restore"));
        Assert.Equal("started", restored.NewStatus);
        Assert.Equal("dnf", restored.PreviousStatus);
    }

    [Fact]
    public async Task OwnerAndAdminCanOperateWithoutStaffAssignment()
    {
        staff.ListActiveByUserIdAsync(actor, Arg.Any<CancellationToken>()).Returns(Array.Empty<EventStaffMember>());
        current.UserId.Returns(race.OrganizerUserId);
        await Handle(Command());
        current.UserId.Returns(actor);
        current.IsAdmin.Returns(true);
        await Handle(Command("restore"));
        Assert.Equal(RunnerRaceStatus.NotStarted, reg.RaceStatus);
    }

    [Fact]
    public async Task HistoryIncludesCurrentStateAndRecentOperations()
    {
        await Handle(Command("lost", "bib_status"));
        registrations.ListOperationsAsync(reg.Id, Arg.Any<CancellationToken>()).Returns(receipts);
        var result = await new GetRunnerOperationsQueryHandler(events, registrations, staff, current)
            .Handle(new(race.Id, reg.Id), default);
        Assert.Equal(reg.Version, result.Version);
        Assert.Equal("lost", result.BibStatus);
        Assert.Single(result.Operations);
    }

    [Theory]
    [InlineData("", "reason", 1L, true)]
    [InlineData("finished", "reason", 1L, true)]
    [InlineData("dnf", "   ", 1L, true)]
    [InlineData("dnf", "reason", -1L, true)]
    [InlineData("dnf", "reason", null, true)]
    [InlineData("dnf", "reason", 1L, false)]
    public void ValidatorRejectsInvalidRequests(string status, string reason, long? version, bool requestId)
    {
        var command = Command() with { Body = new(status, reason, version, requestId ? Guid.NewGuid() : Guid.Empty) };
        Assert.False(new UpdateRunnerStatusCommandValidator().Validate(command).IsValid);
    }

    [Fact]
    public async Task DqRemovesRunnerFromLeaderboard_RestoreRestoresRanking()
    {
        var distance = EventDistance.Create(race.Id, "10K", 10, 100, 100000);
        race.ReplaceDistances([distance]);
        var winner = RunnerOperationsTests.Registration(race.Id, distance.Id);
        var other = RunnerOperationsTests.Registration(race.Id, distance.Id);
        winner.Finish(DateTime.UtcNow, 1000);
        other.Finish(DateTime.UtcNow, 1200);
        registrations.ListByEventAsync(race.Id, Arg.Any<CancellationToken>()).Returns(new[] { winner, other });
        events.GetByIdWithDetailsAsync(race.Id, Arg.Any<CancellationToken>()).Returns(race);
        var handler = new GetDistanceLeaderboardQueryHandler(events, registrations);
        winner.SetRaceStatus(RunnerRaceStatus.Dq, false);
        var result = await handler.Handle(new(race.Id, distance.Id, null), default);
        Assert.Single(result.Items);
        Assert.Equal(1, result.Items[0].Rank);
        winner.RestoreRaceStatus(true);
        result = await handler.Handle(new(race.Id, distance.Id, null), default);
        Assert.Equal(2, result.Items.Count);
        Assert.Equal("00:16:40", result.Items[0].TotalTime);
    }
}
