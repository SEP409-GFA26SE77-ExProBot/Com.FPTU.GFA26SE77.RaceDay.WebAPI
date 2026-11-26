using MediatR;
using NSubstitute;
using RaceDay.Modules.Event.Application.Abstractions;
using RaceDay.Modules.Event.Application.Staff;
using RaceDay.Modules.Event.Domain.Events;
using RaceDay.Modules.Event.Domain.Staff;
using RaceDay.Modules.Map.Contracts.Courses;
using RaceDay.Shared.Application.Abstractions;
using RaceDay.Shared.Domain.Exceptions;

namespace RaceDay.Modules.Event.UnitTests.Application;

public sealed class StaffAssignmentValidationTests
{
    private readonly IRaceCourseReadQuery _courses = Substitute.For<IRaceCourseReadQuery>();
    private readonly IRaceEventRepository _events = Substitute.For<IRaceEventRepository>();
    private readonly IEventStaffMemberRepository _staff = Substitute.For<IEventStaffMemberRepository>();
    private readonly IEventUnitOfWork _uow = Substitute.For<IEventUnitOfWork>();
    private readonly IMediator _mediator = Substitute.For<IMediator>();
    private readonly ICurrentUserAccessor _currentUser = Substitute.For<ICurrentUserAccessor>();
    private readonly CancellationToken _ct = CancellationToken.None;
    private readonly Guid _distanceId = Guid.NewGuid();
    private readonly RaceEvent _event;

    public StaffAssignmentValidationTests()
    {
        _event = RaceEvent.CreateDraft(Guid.NewGuid(), "Race", "Hanoi", new DateOnly(2027, 1, 1),
            new GeoPoint(21, 105), OrganizerInfo.Create("Organizer", "org@example.com", "0900000000"), 100);
        _currentUser.UserId.Returns(_event.OrganizerUserId);
        _events.GetByIdAsync(_event.Id, _ct).Returns(_event);
        _courses.GetAsync(_event.Id, _distanceId, _ct).Returns(new RaceCourseSnapshot(
            _event.Id, _distanceId, "10K", 10, [],
            [new("checkpoint", "Checkpoint 1", 2, 21, 105, "cp"), new("aid", "Aid station", 5, 21, 105, "aid")]));
    }

    [Theory]
    [InlineData("race-staff", "duty:broom")]
    [InlineData("photographer", "duty:float")]
    [InlineData("race-staff", null)]
    public async Task Add_AcceptsRoleDutyAndUnassigned(string role, string? stationId)
    {
        var handler = new AddEventStaffCommandHandler(_courses, _events, _staff, _uow, _currentUser);

        var result = await handler.Handle(new(_event.Id, new(role, "Staff", null, null, stationId, null)), _ct);

        Assert.Equal(stationId, result.StationId);
        await _uow.Received(1).SaveChangesAsync(_ct);
    }

    [Theory]
    [InlineData("race-staff", "duty:float")]
    [InlineData("race-staff", "duty:ops")]
    [InlineData("race-staff", "duty:checkin")]
    [InlineData("race-staff", "duty:kit")]
    [InlineData("photographer", "duty:broom")]
    [InlineData("photographer", "duty:static")]
    public async Task Invite_RejectsDisallowedDutyInStationListBeforeProvisioning(string role, string stationId)
    {
        var handler = new InviteEventStaffCommandHandler(_courses, _events, _staff, _uow, _currentUser, _mediator);

        await Assert.ThrowsAsync<DomainException>(() => handler.Handle(
            new(_event.Id, new(role, null, "Staff", null, null, null, [new(stationId, null)])), _ct));

        Assert.Empty(_mediator.ReceivedCalls());
    }

    [Theory]
    [InlineData(EventStaffRole.RaceStaff)]
    [InlineData(EventStaffRole.Photographer)]
    public async Task Assign_AcceptsCheckpointAndRejectsSupportStation(EventStaffRole role)
    {
        var member = ExistingStaff(role);
        var handler = new AssignEventStaffStationCommandHandler(_courses, _events, _staff, _uow, _mediator, _currentUser);

        var checkpoint = $"map:{_distanceId}:checkpoint";
        var result = await handler.Handle(new(_event.Id, member.Id, new(null, null, [new(checkpoint, "Checkpoint 1")])), _ct);
        Assert.Equal(checkpoint, result.StationId);

        var aid = $"map:{_distanceId}:aid";
        await Assert.ThrowsAsync<DomainException>(() => handler.Handle(
            new(_event.Id, member.Id, new(null, null, [new(aid, "Aid station")])), _ct));
        Assert.Equal(checkpoint, member.StationId);
    }

    [Fact]
    public async Task Update_PreservesLegacyAssignmentDuringProfileEditAndRejectsNewDisallowedDuty()
    {
        var member = ExistingStaff(EventStaffRole.RaceStaff, "duty:ops", "Operations");
        var handler = new UpdateEventStaffCommandHandler(_courses, _events, _staff, _uow, _mediator, _currentUser);

        var result = await handler.Handle(new(_event.Id, member.Id,
            new("Updated Name", null, null, null, null, [new("duty:ops", null)])), _ct);
        Assert.Equal("Updated Name", result.FullName);
        Assert.Equal("Operations", result.StationLabel);

        await Assert.ThrowsAsync<DomainException>(() => handler.Handle(
            new(_event.Id, member.Id, new("Invalid Name", null, null, null, null, [new("duty:float", null)])), _ct));
        Assert.Equal("Updated Name", member.FullName);
        Assert.Equal("duty:ops", member.StationId);
    }

    private EventStaffMember ExistingStaff(EventStaffRole role, string? stationId = null, string? stationLabel = null)
    {
        var member = EventStaffMember.AddDirect(_event.Id, role, "Staff", null, null, stationId, stationLabel);
        _staff.GetByIdAsync(member.Id, _ct).Returns(member);
        return member;
    }
}
