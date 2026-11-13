using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using RaceDay.Modules.Event.Application.Abstractions;
using RaceDay.Modules.Event.Application.Registrations;
using RaceDay.Modules.Event.Domain.Events;
using RaceDay.Modules.Event.Domain.Registrations;
using RaceDay.Shared.Application.Abstractions;
using RaceDay.Shared.Domain.Exceptions;

namespace RaceDay.Modules.Event.UnitTests.Application;

[Trait("Workflow", "WF-02")]
public class CheckInWorkflowTests
{
    [Fact]
    [Trait("Feature", "MF-05")]
    public async Task Approve_Paid_Registration_Assigns_Bib_And_CheckIn_Qr()
    {
        var h = new Harness(RegistrationStatus.PendingApproval);
        h.Registrations.NextBibNumberAsync(h.Race.Id, h.Distance.Id, Arg.Any<CancellationToken>())
            .Returns("21K0001");

        var ticket = await h.Approve.Handle(new ApproveRegistrationCommand(h.Registration.Id, null), CancellationToken.None);

        Assert.Equal("confirmed", ticket.Status);
        Assert.Equal("21K0001", ticket.BibNumber);
        Assert.NotNull(ticket.TicketCode);
        Assert.NotNull(ticket.QrPayload);
        var qr = JsonSerializer.Serialize(ticket.QrPayload);
        using var document = JsonDocument.Parse(qr);
        Assert.Equal(h.Race.Id, document.RootElement.GetProperty("event_id").GetGuid());
        Assert.Equal("21K0001", document.RootElement.GetProperty("bib").GetString());
        await h.UnitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());

        var result = await h.CheckIn.Handle(new CheckInRegistrationCommand(h.Race.Id, null, qr), CancellationToken.None);

        Assert.Equal(h.Registration.Id, result.RegistrationId);
        Assert.False(result.AlreadyCheckedIn);
        Assert.Equal(h.OrganizerId, h.Registration.CheckedInByUserId);
        await h.UnitOfWork.Received(2).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    [Trait("Feature", "MF-05")]
    public async Task Approve_Rejects_Bib_Already_Assigned_To_Another_Runner()
    {
        var h = new Harness(RegistrationStatus.PendingApproval);
        var other = h.CreateRegistration();
        other.Confirm("21K0001");
        h.Registrations.GetByEventAndBibAsync(h.Race.Id, "21K0001", Arg.Any<CancellationToken>()).Returns(other);

        var error = await Assert.ThrowsAsync<DomainException>(() =>
            h.Approve.Handle(new ApproveRegistrationCommand(h.Registration.Id, "21K0001"), CancellationToken.None));

        Assert.Contains("đã được cấp", error.Message);
        Assert.Equal(RegistrationStatus.PendingApproval, h.Registration.Status);
        Assert.Null(h.Registration.BibNumber);
        h.Registrations.DidNotReceive().Update(Arg.Any<EventRegistration>());
        await h.UnitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    [Trait("Feature", "MF-05")]
    public async Task Approve_Rejects_Bib_Outside_Configured_Range()
    {
        var h = new Harness(RegistrationStatus.PendingApproval);

        await Assert.ThrowsAsync<DomainException>(() =>
            h.Approve.Handle(new ApproveRegistrationCommand(h.Registration.Id, "21K9999"), CancellationToken.None));

        Assert.Equal(RegistrationStatus.PendingApproval, h.Registration.Status);
        await h.UnitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(RegistrationStatus.Rejected)]
    [InlineData(RegistrationStatus.Cancelled)]
    [InlineData(RegistrationStatus.Finished)]
    [Trait("Feature", "MF-05")]
    public async Task Approve_Rejects_Ineligible_Registration(RegistrationStatus status)
    {
        var h = new Harness(status);
        h.Registrations.GetByEventAndBibAsync(h.Race.Id, "21K0002", Arg.Any<CancellationToken>())
            .Returns((EventRegistration?)null);

        await Assert.ThrowsAsync<DomainException>(() =>
            h.Approve.Handle(new ApproveRegistrationCommand(h.Registration.Id, "21K0002"), CancellationToken.None));

        Assert.Equal(status, h.Registration.Status);
        await h.UnitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("organizer")]
    [InlineData("staff")]
    [InlineData("admin")]
    [Trait("Feature", "MF-06")]
    public async Task Authorized_Actor_Checks_In_By_Bib(string actor)
    {
        var h = new Harness();
        var actorId = actor == "organizer" ? h.OrganizerId : Guid.NewGuid();
        h.Current.UserId.Returns(actorId);
        h.Current.IsAdmin.Returns(actor == "admin");
        h.Staff.ExistsActiveStaffUserAsync(h.Race.Id, actorId, Arg.Any<CancellationToken>())
            .Returns(actor == "staff");

        var result = await h.CheckIn.Handle(new CheckInRegistrationCommand(h.Race.Id, " 21K0001 ", null), CancellationToken.None);

        Assert.Equal(h.Registration.Id, result.RegistrationId);
        Assert.Equal("confirmed", result.Status);
        Assert.False(result.AlreadyCheckedIn);
        Assert.NotEqual(default, result.CheckedInAtUtc);
        Assert.Equal(actorId, h.Registration.CheckedInByUserId);
        Assert.Equal(21, result.DistanceKm);
        h.Registrations.Received(1).Update(h.Registration);
        await h.UnitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    [Trait("Feature", "MF-06")]
    public async Task Qr_From_Another_Event_Is_Rejected_Before_Registration_Lookup()
    {
        var h = new Harness();
        var qr = JsonSerializer.Serialize(new { event_id = Guid.NewGuid(), bib = "21K0001" });

        var error = await Assert.ThrowsAsync<DomainException>(() =>
            h.CheckIn.Handle(new CheckInRegistrationCommand(h.Race.Id, null, qr), CancellationToken.None));

        Assert.Equal("QR không thuộc giải đang check-in.", error.Message);
        await h.Registrations.DidNotReceive().GetByEventAndBibAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await h.UnitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("not-json")]
    [InlineData("{}")]
    [InlineData("{\"bib\":\"unknown\"}")]
    [Trait("Feature", "MF-06")]
    public async Task Invalid_Or_Unknown_Qr_Does_Not_Record_CheckIn(string qr)
    {
        var h = new Harness();

        await Assert.ThrowsAsync<DomainException>(() =>
            h.CheckIn.Handle(new CheckInRegistrationCommand(h.Race.Id, null, qr), CancellationToken.None));

        Assert.Null(h.Registration.CheckedInAtUtc);
        await h.UnitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    [Trait("Feature", "MF-06")]
    public async Task Unassigned_User_Cannot_Check_In_An_Event()
    {
        var h = new Harness();
        h.Current.UserId.Returns(Guid.NewGuid());

        var error = await Assert.ThrowsAsync<DomainException>(() =>
            h.CheckIn.Handle(new CheckInRegistrationCommand(h.Race.Id, "21K0001", null), CancellationToken.None));

        Assert.Equal("Bạn không có quyền check-in giải này.", error.Message);
        await h.Registrations.DidNotReceive().GetByEventAndBibAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await h.UnitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    [Trait("Feature", "MF-06")]
    public async Task Anonymous_User_Cannot_Check_In()
    {
        var h = new Harness();
        h.Current.UserId.Returns((Guid?)null);

        await Assert.ThrowsAsync<DomainException>(() =>
            h.CheckIn.Handle(new CheckInRegistrationCommand(h.Race.Id, "21K0001", null), CancellationToken.None));

        await h.Events.DidNotReceive().GetByIdWithDetailsAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(RegistrationStatus.PendingPayment)]
    [InlineData(RegistrationStatus.PendingApproval)]
    [InlineData(RegistrationStatus.Rejected)]
    [InlineData(RegistrationStatus.Cancelled)]
    [InlineData(RegistrationStatus.Finished)]
    [Trait("Feature", "MF-06")]
    public async Task Ineligible_Registration_Cannot_Check_In(RegistrationStatus status)
    {
        var h = new Harness(status);

        await Assert.ThrowsAsync<DomainException>(() =>
            h.CheckIn.Handle(new CheckInRegistrationCommand(h.Race.Id, "21K0001", null), CancellationToken.None));

        Assert.Null(h.Registration.CheckedInAtUtc);
        Assert.Null(h.Registration.CheckedInByUserId);
        await h.UnitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    [Trait("Feature", "MF-06")]
    public async Task Replayed_CheckIn_Preserves_First_Timestamp_And_Staff()
    {
        var h = new Harness();
        var firstStaff = Guid.NewGuid();
        h.Registration.CheckIn(firstStaff);
        var firstTimestamp = h.Registration.CheckedInAtUtc;

        var result = await h.CheckIn.Handle(new CheckInRegistrationCommand(h.Race.Id, "21K0001", null), CancellationToken.None);

        Assert.True(result.AlreadyCheckedIn);
        Assert.Equal(firstTimestamp, result.CheckedInAtUtc);
        Assert.Equal(firstStaff, h.Registration.CheckedInByUserId);
        h.Registrations.DidNotReceive().Update(Arg.Any<EventRegistration>());
        await h.UnitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    [Trait("Feature", "MF-06")]
    public async Task Qr_With_User_Id_Can_Resolve_Confirmed_Registration()
    {
        var h = new Harness();
        h.Registrations.GetConfirmedByEventAndUserAsync(h.Race.Id, h.Registration.UserId, Arg.Any<CancellationToken>())
            .Returns(h.Registration);
        var qr = JsonSerializer.Serialize(new { event_id = h.Race.Id, user_id = h.Registration.UserId });

        var result = await h.CheckIn.Handle(new CheckInRegistrationCommand(h.Race.Id, null, qr), CancellationToken.None);

        Assert.Equal(h.Registration.Id, result.RegistrationId);
        Assert.False(result.AlreadyCheckedIn);
        await h.UnitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    private sealed class Harness
    {
        public Guid OrganizerId { get; } = Guid.NewGuid();
        public RaceEvent Race { get; }
        public EventDistance Distance { get; }
        public EventRegistration Registration { get; }
        public IRaceEventRepository Events { get; } = Substitute.For<IRaceEventRepository>();
        public IEventRegistrationRepository Registrations { get; } = Substitute.For<IEventRegistrationRepository>();
        public IEventStaffMemberRepository Staff { get; } = Substitute.For<IEventStaffMemberRepository>();
        public ICurrentUserAccessor Current { get; } = Substitute.For<ICurrentUserAccessor>();
        public IEventUnitOfWork UnitOfWork { get; } = Substitute.For<IEventUnitOfWork>();
        public CheckInRegistrationCommandHandler CheckIn { get; }
        public ApproveRegistrationCommandHandler Approve { get; }

        public Harness(RegistrationStatus status = RegistrationStatus.Confirmed)
        {
            Race = RaceEvent.CreateDraft(OrganizerId, "Race-Day Test", "Da Nang", new DateOnly(2026, 12, 14),
                new GeoPoint(16.05, 108.2), OrganizerInfo.Create("Organizer", "org@raceday.vn", "0901234567"), 10);
            Distance = EventDistance.Create(Race.Id, "Half Marathon", 21, 10, 500_000, type: "21K");
            Distance.SetBibConfig("21K", 1, 10);
            Race.ReplaceDistances([Distance]);
            Registration = CreateRegistration();
            if (status == RegistrationStatus.Cancelled)
                Registration.CancelUnpaid();
            else if (status != RegistrationStatus.PendingPayment)
            {
                Registration.MarkPaidPendingApproval();
                if (status == RegistrationStatus.Rejected)
                    Registration.Reject("Missing document");
                else if (status is RegistrationStatus.Confirmed or RegistrationStatus.Finished)
                {
                    Registration.Confirm("21K0001");
                    if (status == RegistrationStatus.Finished)
                        Registration.Finish(DateTime.UtcNow, 3600);
                }
            }

            Current.UserId.Returns(OrganizerId);
            Events.GetByIdWithDetailsAsync(Race.Id, Arg.Any<CancellationToken>()).Returns(Race);
            Registrations.GetByIdAsync(Registration.Id, Arg.Any<CancellationToken>()).Returns(Registration);
            Registrations.GetByEventAndBibAsync(Race.Id, "21K0001", Arg.Any<CancellationToken>()).Returns(Registration);
            CheckIn = new CheckInRegistrationCommandHandler(Events, Registrations, Staff, Current, UnitOfWork);
            Approve = new ApproveRegistrationCommandHandler(Events, Registrations, Current, UnitOfWork,
                Substitute.For<IEventBus>(), Substitute.For<IEmailSender>(), NullLogger<ApproveRegistrationCommandHandler>.Instance);
        }

        public EventRegistration CreateRegistration() => EventRegistration.Create(
            Race.Id, Distance.Id, Guid.NewGuid(), "Runner", "runner@raceday.vn", "0901234567",
            null, null, null, null, null, null, null, null, null, null, null, "[]", null, null, true);
    }
}
