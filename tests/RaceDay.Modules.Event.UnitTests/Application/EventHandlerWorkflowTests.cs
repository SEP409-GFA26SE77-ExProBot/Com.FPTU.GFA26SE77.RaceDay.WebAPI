using NSubstitute;
using RaceDay.Modules.Event.Application.Abstractions;
using RaceDay.Modules.Event.Application.Events;
using RaceDay.Modules.Event.Application.Registrations;
using RaceDay.Modules.Event.Domain.Events;
using RaceDay.Modules.Event.Domain.Registrations;
using RaceDay.Shared.Application.Abstractions;
using RaceDay.Shared.Domain.Exceptions;

namespace RaceDay.Modules.Event.UnitTests.Application;

public class EventHandlerWorkflowTests
{
    private static readonly Guid OrganizerId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid UserId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    private static RaceEvent PendingRace(DateOnly? eventDate = null, TimeOnly? startTime = null)
    {
        var race = RaceEvent.CreateDraft(
            OrganizerId, "Fansipan Sky Race", "Sa Pa", eventDate ?? new DateOnly(2026, 12, 14),
            new GeoPoint(22.3, 103.8), OrganizerInfo.Create("VietTrail", "admin@viettrail.vn", "02143871999"),
            800, startTime: startTime, tagline: "Roof of Indochina");
        var distance = EventDistance.Create(
            race.Id, "21K", "Cloud Valley", 21, 980, 2150, 1450, 500, 900_000, "VND", "#F59E0B",
            new NamedGeoPoint("Square", 22.3, 103.8), new NamedGeoPoint("Square", 22.3, 103.8), "{}");
        race.ReplaceDistances([distance]);
        race.ReplaceDocuments([
            EventDocument.Create(race.Id, "official-letter", "Official letter", "official-letter.pdf", "/official-letter.pdf", true),
            EventDocument.Create(race.Id, "security", "Security plan", "security.pdf", "/security.pdf", true),
            EventDocument.Create(race.Id, "plan", "Plan", "a.pdf", "/a.pdf", true)]);
        race.SubmitForApproval();
        race.ClearDomainEvents();
        return race;
    }

    [Fact]
    public async Task ApproveRaceEvent_Opens_Registration()
    {
        var race = PendingRace();
        var repo = Substitute.For<IRaceEventRepository>();
        var uow = Substitute.For<IEventUnitOfWork>();
        var bus = Substitute.For<IEventBus>();
        repo.GetByIdWithDetailsAsync(race.Id, Arg.Any<CancellationToken>()).Returns(race);

        var result = await new ApproveRaceEventCommandHandler(repo, uow, bus)
            .Handle(new ApproveRaceEventCommand(race.Id, OpenRegistration: true), CancellationToken.None);

        Assert.Equal("registration-open", result.Status);
        await uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RejectRaceEvent_Sets_Rejected()
    {
        var race = PendingRace();
        var repo = Substitute.For<IRaceEventRepository>();
        repo.GetByIdWithDetailsAsync(race.Id, Arg.Any<CancellationToken>()).Returns(race);

        var result = await new RejectRaceEventCommandHandler(repo, Substitute.For<IEventUnitOfWork>(), Substitute.For<IEventBus>())
            .Handle(new RejectRaceEventCommand(race.Id, "Thiếu giấy phép"), CancellationToken.None);

        Assert.Equal("rejected", result.Status);
        Assert.Equal("Thiếu giấy phép", result.RejectionReason);
    }

    [Fact]
    public async Task CreateRegistration_Requires_Open_Event()
    {
        var race = PendingRace();
        race.Approve();
        var distanceId = race.Distances.First().Id;

        var events = Substitute.For<IRaceEventRepository>();
        var regs = Substitute.For<IEventRegistrationRepository>();
        var current = Substitute.For<ICurrentUserAccessor>();
        events.GetByIdWithDetailsAsync(race.Id, Arg.Any<CancellationToken>()).Returns(race);
        current.UserId.Returns(UserId);

        await Assert.ThrowsAsync<DomainException>(() =>
            new CreateRegistrationCommandHandler(events, regs, current, Substitute.For<IEventUnitOfWork>(), Substitute.For<IEventBus>())
                .Handle(new CreateRegistrationCommand(
                    race.Id, distanceId, "Runner", "r@raceday.vn", "0901111222",
                    null, null, null, null, null, null, null, null, null, null, null, null, null, null, true),
                    CancellationToken.None));
    }

    [Fact]
    public async Task CreateRegistration_Success_When_Open()
    {
        var race = PendingRace();
        race.Approve();
        race.OpenRegistration();
        var distanceId = race.Distances.First().Id;

        var events = Substitute.For<IRaceEventRepository>();
        var regs = Substitute.For<IEventRegistrationRepository>();
        var current = Substitute.For<ICurrentUserAccessor>();
        var uow = Substitute.For<IEventUnitOfWork>();
        var bus = Substitute.For<IEventBus>();

        events.GetByIdWithDetailsAsync(race.Id, Arg.Any<CancellationToken>()).Returns(race);
        regs.ExistsForUserDistanceAsync(race.Id, distanceId, UserId, Arg.Any<CancellationToken>()).Returns(false);
        current.UserId.Returns(UserId);

        var result = await new CreateRegistrationCommandHandler(events, regs, current, uow, bus)
            .Handle(new CreateRegistrationCommand(
                race.Id, distanceId, "Runner", "r@raceday.vn", "0901111222",
                new DateOnly(1998, 5, 1), "Male", "001", 170, 60, "A+",
                "Em", "090", "Áo thun", "M", "Đỏ", ["none"], "c.pdf", "/c.pdf", true),
                CancellationToken.None);

        Assert.Equal("pending_payment", result.Status);
        await regs.Received(1).AddAsync(Arg.Any<EventRegistration>(), Arg.Any<CancellationToken>());
        await uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Organizer_ApproveRegistration()
    {
        var race = PendingRace();
        race.Approve();
        race.OpenRegistration();
        var distanceId = race.Distances.First().Id;
        var reg = EventRegistration.Create(
            race.Id, distanceId, UserId, "R", "r@raceday.vn", "090", null, null, null,
            null, null, null, null, null, null, null, null, "[]", null, null, true);
        reg.MarkPaidPendingApproval();

        var events = Substitute.For<IRaceEventRepository>();
        var regs = Substitute.For<IEventRegistrationRepository>();
        var current = Substitute.For<ICurrentUserAccessor>();
        events.GetByIdWithDetailsAsync(race.Id, Arg.Any<CancellationToken>()).Returns(race);
        regs.GetByIdAsync(reg.Id, Arg.Any<CancellationToken>()).Returns(reg);
        current.UserId.Returns(OrganizerId);

        var result = await new ApproveRegistrationCommandHandler(
                events,
                regs,
                current,
                Substitute.For<IEventUnitOfWork>(),
                Substitute.For<IEventBus>(),
                Substitute.For<IEmailSender>(),
                Substitute.For<Microsoft.Extensions.Logging.ILogger<ApproveRegistrationCommandHandler>>())
            .Handle(new ApproveRegistrationCommand(reg.Id, "BIB-1"), CancellationToken.None);

        Assert.Equal("confirmed", result.Status);
        Assert.Equal("BIB-1", result.BibNumber);
    }

    [Fact]
    public async Task FinishRegistration_Sets_Finished_Status_And_Result()
    {
        var race = PendingRace(DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-2)), new TimeOnly(5, 0));
        race.Approve();
        race.OpenRegistration();
        var distanceId = race.Distances.First().Id;
        var reg = EventRegistration.Create(
            race.Id, distanceId, UserId, "Runner", "runner@raceday.vn", "0901111222", null, null, null,
            null, null, null, null, null, null, null, null, "[]", null, null, true);
        reg.Confirm("BIB-9");

        var events = Substitute.For<IRaceEventRepository>();
        var regs = Substitute.For<IEventRegistrationRepository>();
        var current = Substitute.For<ICurrentUserAccessor>();
        var uow = Substitute.For<IEventUnitOfWork>();
        events.GetByIdWithDetailsAsync(race.Id, Arg.Any<CancellationToken>()).Returns(race);
        regs.GetByEventAndBibAsync(race.Id, "BIB-9", Arg.Any<CancellationToken>()).Returns(reg);
        current.UserId.Returns(OrganizerId);

        var result = await new FinishRegistrationCommandHandler(events, regs, Substitute.For<IEventStaffMemberRepository>(), current, uow)
            .Handle(new FinishRegistrationCommand(race.Id, "BIB-9", null), CancellationToken.None);

        Assert.Equal("finished", result.Status);
        Assert.False(result.AlreadyFinished);
        Assert.True(result.ResultSeconds > 0);
        Assert.NotEqual(default, result.FinishedAtUtc);
        await uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}
