using NSubstitute;
using RaceDay.Modules.Device.Contracts.Sos;
using RaceDay.Modules.Event.Contracts.Events;
using RaceDay.Modules.RaceMonitoring.Application.Abstractions;
using RaceDay.Modules.RaceMonitoring.Application.EmergencyAssignments;
using RaceDay.Modules.RaceMonitoring.Application.EmergencyResources;
using RaceDay.Modules.RaceMonitoring.Application.EmergencyResponses;
using RaceDay.Modules.RaceMonitoring.Domain;
using RaceDay.Modules.RaceMonitoring.Domain.EmergencyCoordination;
using RaceDay.Shared.Application.Abstractions;
using RaceDay.Shared.Domain.Exceptions;

namespace RaceDay.Modules.RaceMonitoring.UnitTests.Application;

public class EmergencyCoordinationHandlerTests
{
    private static readonly Guid UserId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid EventId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
    private static readonly Guid OtherEventId = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");
    private const string SourceSosStatus = "Acknowledged";

    [Fact]
    public async Task ListResources_Returns_Event_Resources_For_Coordinator()
    {
        var resource = Resource(EventId, EmergencyResourceStatus.Available);
        var (resources, access, sosOps, current) = ResourceAccess();
        resources.ListByEventAsync(
                EventId,
                EmergencyResourceStatus.Available,
                null,
                1,
                20,
                Arg.Any<CancellationToken>())
            .Returns(([resource], 1));

        var result = await new ListEmergencyResourcesQueryHandler(resources, access, sosOps, current)
            .Handle(new ListEmergencyResourcesQuery(EventId, "available", null), CancellationToken.None);

        var item = Assert.Single(result.Items);
        Assert.Equal(EventId, item.EventId);
        Assert.Equal(resource.ResourceCode, item.ResourceCode);
        Assert.Equal("Available", item.Status);
        await sosOps.Received(1).CanManageSosAsync(EventId, UserId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ListResources_Rejects_Admin_Who_Is_Not_Event_Staff()
    {
        var (resources, access, sosOps, current) = ResourceAccess(allowed: false, admin: true);

        var ex = await Assert.ThrowsAsync<DomainException>(() =>
            new ListEmergencyResourcesQueryHandler(resources, access, sosOps, current)
                .Handle(new ListEmergencyResourcesQuery(EventId, null, null), CancellationToken.None));

        Assert.Equal("Chỉ organizer của giải hoặc Coordinator Active mới điều phối nguồn lực.", ex.Message);
        await resources.DidNotReceive().ListByEventAsync(
            Arg.Any<Guid>(),
            Arg.Any<EmergencyResourceStatus?>(),
            Arg.Any<EmergencyResourceType?>(),
            Arg.Any<int>(),
            Arg.Any<int>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ListResources_Rejects_Coordinator_Outside_Event()
    {
        var (resources, access, sosOps, current) = ResourceAccess();
        access.GetAsync(OtherEventId, Arg.Any<CancellationToken>())
            .Returns(new EventOrganizerAccessInfo(OtherEventId, Guid.NewGuid(), "RegistrationOpen", "Khác"));
        sosOps.CanManageSosAsync(OtherEventId, UserId, Arg.Any<CancellationToken>()).Returns(false);

        var ex = await Assert.ThrowsAsync<DomainException>(() =>
            new ListEmergencyResourcesQueryHandler(resources, access, sosOps, current)
                .Handle(new ListEmergencyResourcesQuery(OtherEventId, null, null), CancellationToken.None));

        Assert.Equal("Chỉ organizer của giải hoặc Coordinator Active mới điều phối nguồn lực.", ex.Message);
        await resources.DidNotReceive().ListByEventAsync(
            OtherEventId,
            Arg.Any<EmergencyResourceStatus?>(),
            Arg.Any<EmergencyResourceType?>(),
            Arg.Any<int>(),
            Arg.Any<int>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetResource_Returns_Event_Resource_For_Coordinator()
    {
        var resource = Resource(EventId, EmergencyResourceStatus.Available);
        var (resources, access, sosOps, current) = ResourceAccess();
        resources.GetByIdAsync(resource.Id, Arg.Any<CancellationToken>()).Returns(resource);

        var result = await new GetEmergencyResourceQueryHandler(resources, access, sosOps, current)
            .Handle(new GetEmergencyResourceQuery(resource.Id), CancellationToken.None);

        Assert.Equal(resource.Id, result.Id);
        Assert.Equal(EventId, result.EventId);
        Assert.Equal("MED-1", result.ResourceCode);
        Assert.Equal("MedicalTeam", result.ResourceType);
        Assert.Equal("Đội y tế", result.Name);
        Assert.Equal("Available", result.Status);
        Assert.Equal(EmergencyResourceStatus.Available, resource.Status);
        await sosOps.Received(1).CanManageSosAsync(EventId, UserId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetResource_Rejects_Admin_Who_Is_Not_Event_Staff()
    {
        var resource = Resource(EventId, EmergencyResourceStatus.Available);
        var (resources, access, sosOps, current) = ResourceAccess(allowed: false, admin: true);
        resources.GetByIdAsync(resource.Id, Arg.Any<CancellationToken>()).Returns(resource);

        var ex = await Assert.ThrowsAsync<DomainException>(() =>
            new GetEmergencyResourceQueryHandler(resources, access, sosOps, current)
                .Handle(new GetEmergencyResourceQuery(resource.Id), CancellationToken.None));

        Assert.Equal("Chỉ organizer của giải hoặc Coordinator Active mới điều phối nguồn lực.", ex.Message);
    }

    [Fact]
    public async Task GetResource_Rejects_Missing_Resource()
    {
        var (resources, access, sosOps, current) = ResourceAccess();

        var ex = await Assert.ThrowsAsync<DomainException>(() =>
            new GetEmergencyResourceQueryHandler(resources, access, sosOps, current)
                .Handle(new GetEmergencyResourceQuery(Guid.NewGuid()), CancellationToken.None));

        Assert.Equal("Không tìm thấy nguồn lực.", ex.Message);
        await sosOps.DidNotReceive().CanManageSosAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetResource_Rejects_Resource_With_No_Event()
    {
        var resource = Resource(null, EmergencyResourceStatus.Available);
        var (resources, access, sosOps, current) = ResourceAccess();
        resources.GetByIdAsync(resource.Id, Arg.Any<CancellationToken>()).Returns(resource);

        var ex = await Assert.ThrowsAsync<DomainException>(() =>
            new GetEmergencyResourceQueryHandler(resources, access, sosOps, current)
                .Handle(new GetEmergencyResourceQuery(resource.Id), CancellationToken.None));

        Assert.Equal("Nguồn lực không thuộc giải.", ex.Message);
        await sosOps.DidNotReceive().CanManageSosAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void GetResource_Validator_Rejects_Empty_Id()
    {
        var result = new GetEmergencyResourceQueryValidator().Validate(new GetEmergencyResourceQuery(Guid.Empty));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.ErrorMessage == "Tham chiếu nguồn lực không hợp lệ.");
    }

    [Fact]
    public async Task Create_Assigns_Available_Resource_On_Open_Response_Without_Changing_Sos_Or_Resource()
    {
        var response = OpenResponse();
        var sourceSosAlertId = response.SourceSosAlertId;
        var resource = Resource(EventId, EmergencyResourceStatus.Available);
        var scope = Scope(response);
        var ctx = AssignmentAccess(response, scope);
        ctx.Resources.GetByIdAsync(resource.Id, Arg.Any<CancellationToken>()).Returns(resource);
        ctx.Assignments.ExistsForResponseAsync(response.Id, resource.Id, Arg.Any<CancellationToken>()).Returns(false);

        var result = await CreateHandler(ctx).Handle(
            new CreateEmergencyAssignmentCommand(response.Id, new CreateEmergencyAssignmentRequest(resource.Id, "  Đội 1  ")),
            CancellationToken.None);

        Assert.Equal(EmergencyAssignmentStatus.Assigned.ToString(), result.Status);
        Assert.Equal("Đội 1", result.Notes);
        Assert.Equal(response.Id, result.EmergencyResponseId);
        Assert.Equal(EmergencyResourceStatus.Available, resource.Status);
        Assert.Equal(EmergencyResponseStatus.Open, response.Status);
        Assert.Equal(sourceSosAlertId, response.SourceSosAlertId);
        Assert.Equal(SourceSosStatus, scope.Status);
        ctx.Assignments.Received(1).Add(Arg.Is<EmergencyAssignment>(row =>
            row.EmergencyResponseId == response.Id
            && row.EmergencyResourceId == resource.Id
            && row.Status == EmergencyAssignmentStatus.Assigned));
        await ctx.UnitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        await ctx.Scopes.Received(1).GetAsync(response.SourceSosAlertId, Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(EmergencyResourceStatus.Unavailable)]
    [InlineData(EmergencyResourceStatus.Offline)]
    public async Task Create_Rejects_Unavailable_Resource(EmergencyResourceStatus status)
    {
        var response = OpenResponse();
        var resource = Resource(EventId, status);
        var ctx = AssignmentAccess(response, Scope(response));
        ctx.Resources.GetByIdAsync(resource.Id, Arg.Any<CancellationToken>()).Returns(resource);

        var ex = await Assert.ThrowsAsync<DomainException>(() => CreateHandler(ctx).Handle(
            new CreateEmergencyAssignmentCommand(response.Id, new CreateEmergencyAssignmentRequest(resource.Id, null)),
            CancellationToken.None));

        Assert.Equal("Nguồn lực không khả dụng để phân công.", ex.Message);
        Assert.Equal(status, resource.Status);
        ctx.Assignments.DidNotReceive().Add(Arg.Any<EmergencyAssignment>());
    }

    [Fact]
    public async Task Create_Rejects_Resource_From_Another_Event()
    {
        var response = OpenResponse();
        var resource = Resource(OtherEventId, EmergencyResourceStatus.Available);
        var ctx = AssignmentAccess(response, Scope(response));
        ctx.Resources.GetByIdAsync(resource.Id, Arg.Any<CancellationToken>()).Returns(resource);

        var ex = await Assert.ThrowsAsync<DomainException>(() => CreateHandler(ctx).Handle(
            new CreateEmergencyAssignmentCommand(response.Id, new CreateEmergencyAssignmentRequest(resource.Id, null)),
            CancellationToken.None));

        Assert.Equal("Nguồn lực không thuộc giải của hồ sơ ứng cứu.", ex.Message);
        ctx.Assignments.DidNotReceive().Add(Arg.Any<EmergencyAssignment>());
    }

    [Fact]
    public async Task Create_Rejects_Shared_Resource_With_No_Event()
    {
        var response = OpenResponse();
        var resource = Resource(null, EmergencyResourceStatus.Available);
        var ctx = AssignmentAccess(response, Scope(response));
        ctx.Resources.GetByIdAsync(resource.Id, Arg.Any<CancellationToken>()).Returns(resource);

        var ex = await Assert.ThrowsAsync<DomainException>(() => CreateHandler(ctx).Handle(
            new CreateEmergencyAssignmentCommand(response.Id, new CreateEmergencyAssignmentRequest(resource.Id, null)),
            CancellationToken.None));

        Assert.Equal("Nguồn lực không thuộc giải của hồ sơ ứng cứu.", ex.Message);
    }

    [Fact]
    public async Task Create_Rejects_When_Response_Or_Resource_Is_Missing()
    {
        var response = OpenResponse();
        var missingResponse = AssignmentAccess(response, Scope(response));
        missingResponse.Responses.GetByIdAsync(response.Id, Arg.Any<CancellationToken>())
            .Returns((EmergencyResponse?)null);

        var missingResponseError = await Assert.ThrowsAsync<DomainException>(() => CreateHandler(missingResponse).Handle(
            new CreateEmergencyAssignmentCommand(response.Id, new CreateEmergencyAssignmentRequest(Guid.NewGuid(), null)),
            CancellationToken.None));
        Assert.Equal("Không tìm thấy phản hồi khẩn cấp.", missingResponseError.Message);

        var missingResource = AssignmentAccess(response, Scope(response));
        missingResource.Resources.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns((EmergencyResource?)null);
        var resourceId = Guid.NewGuid();
        var missingResourceError = await Assert.ThrowsAsync<DomainException>(() => CreateHandler(missingResource).Handle(
            new CreateEmergencyAssignmentCommand(response.Id, new CreateEmergencyAssignmentRequest(resourceId, null)),
            CancellationToken.None));
        Assert.Equal("Không tìm thấy nguồn lực.", missingResourceError.Message);
        missingResource.Assignments.DidNotReceive().Add(Arg.Any<EmergencyAssignment>());
    }

    [Fact]
    public async Task Create_Rejects_Response_That_Is_Not_Open()
    {
        var response = EmergencyResponse.Create(
            Guid.NewGuid(),
            EmergencyIncidentType.Medical,
            EmergencyPriority.High,
            EmergencyResponseStatus.Assigned,
            DateTime.UtcNow);
        var ctx = AssignmentAccess(response, Scope(response));

        var ex = await Assert.ThrowsAsync<DomainException>(() => CreateHandler(ctx).Handle(
            new CreateEmergencyAssignmentCommand(response.Id, new CreateEmergencyAssignmentRequest(Guid.NewGuid(), null)),
            CancellationToken.None));

        Assert.Equal("Chỉ hồ sơ đang mở mới được phân công nguồn lực.", ex.Message);
        await ctx.Resources.DidNotReceive().GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Create_Rejects_Resource_Already_On_The_Response()
    {
        var response = OpenResponse();
        var resource = Resource(EventId, EmergencyResourceStatus.Assigned);
        var ctx = AssignmentAccess(response, Scope(response));
        ctx.Resources.GetByIdAsync(resource.Id, Arg.Any<CancellationToken>()).Returns(resource);
        ctx.Assignments.ExistsForResponseAsync(response.Id, resource.Id, Arg.Any<CancellationToken>()).Returns(true);

        var ex = await Assert.ThrowsAsync<DomainException>(() => CreateHandler(ctx).Handle(
            new CreateEmergencyAssignmentCommand(response.Id, new CreateEmergencyAssignmentRequest(resource.Id, null)),
            CancellationToken.None));

        Assert.Equal("Nguồn lực này đã nằm trong danh sách phân công của hồ sơ.", ex.Message);
        Assert.Equal(EmergencyResourceStatus.Assigned, resource.Status);
        ctx.Assignments.DidNotReceive().Add(Arg.Any<EmergencyAssignment>());
    }

    [Fact]
    public async Task Create_Rejects_Response_Without_An_Event()
    {
        var response = OpenResponse();
        var scope = new SosAlertScope(response.SourceSosAlertId, null, SourceSosStatus);
        var ctx = AssignmentAccess(response, scope);

        var ex = await Assert.ThrowsAsync<DomainException>(() => CreateHandler(ctx).Handle(
            new CreateEmergencyAssignmentCommand(response.Id, new CreateEmergencyAssignmentRequest(Guid.NewGuid(), null)),
            CancellationToken.None));

        Assert.Equal("Hồ sơ chưa gắn giải nên không điều phối nguồn lực.", ex.Message);
        Assert.Equal(SourceSosStatus, scope.Status);
        await ctx.Resources.DidNotReceive().GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Update_Rejects_A_Skipped_Transition()
    {
        var response = OpenResponse();
        var assignment = EmergencyAssignment.Create(
            response.Id, Guid.NewGuid(), DateTime.UtcNow, EmergencyAssignmentStatus.Assigned);
        var ctx = AssignmentAccess(response, Scope(response));
        ctx.Assignments.GetByIdAsync(assignment.Id, Arg.Any<CancellationToken>()).Returns(assignment);
        ctx.Responses.GetByIdAsync(response.Id, Arg.Any<CancellationToken>()).Returns(response);
        ctx.Resources.GetByIdAsync(assignment.EmergencyResourceId, Arg.Any<CancellationToken>())
            .Returns(Resource(EventId, EmergencyResourceStatus.Available));

        var ex = await Assert.ThrowsAsync<DomainException>(() =>
            new UpdateEmergencyAssignmentCommandHandler(
                    ctx.Responses, ctx.Resources, ctx.Assignments, ctx.UnitOfWork, ctx.Scopes, ctx.Access, ctx.SosOps, ctx.Current)
                .Handle(
                    new UpdateEmergencyAssignmentCommand(assignment.Id, new UpdateEmergencyAssignmentRequest("Completed")),
                    CancellationToken.None));

        Assert.Equal("Không thể chuyển phân công ở trạng thái hiện tại.", ex.Message);
        Assert.Equal(EmergencyAssignmentStatus.Assigned, assignment.Status);
        await ctx.UnitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Update_Accepts_The_Next_Step()
    {
        var response = OpenResponse();
        var resource = Resource(EventId, EmergencyResourceStatus.Available);
        var assignment = EmergencyAssignment.Create(
            response.Id, resource.Id, DateTime.UtcNow, EmergencyAssignmentStatus.Assigned);
        var scope = Scope(response);
        var ctx = AssignmentAccess(response, scope);
        ctx.Assignments.GetByIdAsync(assignment.Id, Arg.Any<CancellationToken>()).Returns(assignment);
        ctx.Resources.GetByIdAsync(resource.Id, Arg.Any<CancellationToken>()).Returns(resource);

        var result = await new UpdateEmergencyAssignmentCommandHandler(
                ctx.Responses, ctx.Resources, ctx.Assignments, ctx.UnitOfWork, ctx.Scopes, ctx.Access, ctx.SosOps, ctx.Current)
            .Handle(
                new UpdateEmergencyAssignmentCommand(assignment.Id, new UpdateEmergencyAssignmentRequest("accepted")),
                CancellationToken.None);

        Assert.Equal("Accepted", result.Status);
        Assert.NotNull(assignment.AcceptedAtUtc);
        Assert.Equal(SourceSosStatus, scope.Status);
        Assert.Equal(EmergencyResponseStatus.Open, response.Status);
    }

    [Fact]
    public async Task Remove_Deletes_Only_The_Assignment_On_That_Open_Response()
    {
        var response = OpenResponse();
        var assignment = EmergencyAssignment.Create(
            response.Id, Guid.NewGuid(), DateTime.UtcNow, EmergencyAssignmentStatus.Assigned);
        var scope = Scope(response);
        var ctx = AssignmentAccess(response, scope);
        ctx.Assignments.GetByIdAsync(assignment.Id, Arg.Any<CancellationToken>()).Returns(assignment);

        await new RemoveEmergencyAssignmentCommandHandler(
                ctx.Responses, ctx.Assignments, ctx.UnitOfWork, ctx.Scopes, ctx.Access, ctx.SosOps, ctx.Current)
            .Handle(new RemoveEmergencyAssignmentCommand(response.Id, assignment.Id), CancellationToken.None);

        ctx.Assignments.Received(1).Remove(assignment);
        Assert.Equal(EmergencyResponseStatus.Open, response.Status);
        Assert.Equal(SourceSosStatus, scope.Status);
        await ctx.UnitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Remove_Does_Not_Touch_An_Assignment_Of_Another_Response()
    {
        var response = OpenResponse();
        var other = OpenResponse();
        var assignment = EmergencyAssignment.Create(
            other.Id, Guid.NewGuid(), DateTime.UtcNow, EmergencyAssignmentStatus.Assigned);
        var ctx = AssignmentAccess(response, Scope(response));
        ctx.Assignments.GetByIdAsync(assignment.Id, Arg.Any<CancellationToken>()).Returns(assignment);

        var ex = await Assert.ThrowsAsync<DomainException>(() =>
            new RemoveEmergencyAssignmentCommandHandler(
                    ctx.Responses, ctx.Assignments, ctx.UnitOfWork, ctx.Scopes, ctx.Access, ctx.SosOps, ctx.Current)
                .Handle(new RemoveEmergencyAssignmentCommand(response.Id, assignment.Id), CancellationToken.None));

        Assert.Equal("Không tìm thấy phân công.", ex.Message);
        ctx.Assignments.DidNotReceive().Remove(Arg.Any<EmergencyAssignment>());
        Assert.Equal(other.Id, assignment.EmergencyResponseId);
    }

    [Fact]
    public async Task Cancel_Completes_Assignments_And_Leaves_Source_Sos_Untouched()
    {
        var response = OpenResponse();
        var sourceSosAlertId = response.SourceSosAlertId;
        var scope = Scope(response);
        var earlier = new DateTime(2026, 10, 27, 1, 0, 0, DateTimeKind.Utc);
        var alreadyDone = EmergencyAssignment.Create(
            response.Id,
            Guid.NewGuid(),
            earlier,
            EmergencyAssignmentStatus.Completed,
            completedAtUtc: earlier);
        var active = EmergencyAssignment.Create(
            response.Id, Guid.NewGuid(), earlier, EmergencyAssignmentStatus.Assigned);
        var ctx = AssignmentAccess(response, scope);
        ctx.Assignments.ListTrackedByResponseAsync(response.Id, Arg.Any<CancellationToken>())
            .Returns(new List<EmergencyAssignment> { alreadyDone, active });

        var result = await new CancelEmergencyResponseCommandHandler(
                ctx.Responses, ctx.Assignments, ctx.UnitOfWork, ctx.Scopes, ctx.Access, ctx.SosOps, ctx.Current)
            .Handle(
                new CancelEmergencyResponseCommand(response.Id, new CancelEmergencyResponseRequest("  Đã xử lý tại chỗ.  ")),
                CancellationToken.None);

        Assert.Equal("Cancelled", result.Status);
        Assert.Equal("Đã xử lý tại chỗ.", result.CancellationReason);
        Assert.Equal(sourceSosAlertId, result.SourceSosAlertId);
        Assert.Equal(SourceSosStatus, result.SourceSosStatus);
        Assert.Equal(SourceSosStatus, scope.Status);
        Assert.Equal(EmergencyAssignmentStatus.Completed, active.Status);
        Assert.Equal(response.CancelledAtUtc, active.CompletedAtUtc);
        Assert.Equal(earlier, alreadyDone.CompletedAtUtc);
        Assert.Equal(EmergencyAssignmentStatus.Completed, alreadyDone.Status);
        await ctx.Scopes.Received(1).GetAsync(sourceSosAlertId, Arg.Any<CancellationToken>());
        await ctx.UnitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Cancel_Resolved_Response_Does_Not_Load_Assignments()
    {
        var response = EmergencyResponse.Create(
            Guid.NewGuid(),
            EmergencyIncidentType.Medical,
            EmergencyPriority.High,
            EmergencyResponseStatus.Resolved,
            DateTime.UtcNow,
            DateTime.UtcNow,
            "Đã xong.");
        var ctx = AssignmentAccess(response, Scope(response));

        var ex = await Assert.ThrowsAsync<DomainException>(() =>
            new CancelEmergencyResponseCommandHandler(
                    ctx.Responses, ctx.Assignments, ctx.UnitOfWork, ctx.Scopes, ctx.Access, ctx.SosOps, ctx.Current)
                .Handle(
                    new CancelEmergencyResponseCommand(response.Id, new CancelEmergencyResponseRequest("Muộn")),
                    CancellationToken.None));

        Assert.Equal("Phản hồi khẩn cấp không thể hủy ở trạng thái hiện tại.", ex.Message);
        Assert.Equal(EmergencyResponseStatus.Resolved, response.Status);
        await ctx.Assignments.DidNotReceive().ListTrackedByResponseAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        await ctx.UnitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Update_Validator_Rejects_Cancelled_Status()
    {
        var result = new UpdateEmergencyAssignmentCommandValidator().Validate(
            new UpdateEmergencyAssignmentCommand(Guid.NewGuid(), new UpdateEmergencyAssignmentRequest("Cancelled")));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error =>
            error.ErrorMessage == "Chỉ được chuyển phân công sang Accepted, Arrived hoặc Completed.");
    }

    private static CreateEmergencyAssignmentCommandHandler CreateHandler(CoordinationContext ctx) =>
        new(
            ctx.Responses,
            ctx.Resources,
            ctx.Assignments,
            ctx.UnitOfWork,
            ctx.Scopes,
            ctx.Access,
            ctx.SosOps,
            ctx.Current);

    private static EmergencyResponse OpenResponse() =>
        EmergencyResponse.Create(
            Guid.NewGuid(),
            EmergencyIncidentType.Medical,
            EmergencyPriority.High,
            EmergencyResponseStatus.Open);

    private static EmergencyResource Resource(Guid? eventId, EmergencyResourceStatus status) =>
        EmergencyResource.Create(eventId, "MED-1", EmergencyResourceType.MedicalTeam, "Đội y tế", status);

    private static SosAlertScope Scope(EmergencyResponse response) =>
        new(response.SourceSosAlertId, EventId, SourceSosStatus);

    private static (IEmergencyResourceRepository Resources, IEventOrganizerAccessQuery Access, IEventSosOpsAccessQuery SosOps, ICurrentUserAccessor Current)
        ResourceAccess(bool allowed = true, bool admin = false)
    {
        var resources = Substitute.For<IEmergencyResourceRepository>();
        var access = Substitute.For<IEventOrganizerAccessQuery>();
        var sosOps = Substitute.For<IEventSosOpsAccessQuery>();
        ICurrentUserAccessor current = new TestUser(UserId, admin);
        access.GetAsync(EventId, Arg.Any<CancellationToken>())
            .Returns(new EventOrganizerAccessInfo(EventId, UserId, "RegistrationOpen", "Demo"));
        sosOps.CanManageSosAsync(EventId, UserId, Arg.Any<CancellationToken>()).Returns(allowed);
        return (resources, access, sosOps, current);
    }

    private static CoordinationContext AssignmentAccess(EmergencyResponse response, SosAlertScope scope, bool allowed = true)
    {
        var responses = Substitute.For<IEmergencyResponseRepository>();
        var resources = Substitute.For<IEmergencyResourceRepository>();
        var assignments = Substitute.For<IEmergencyAssignmentRepository>();
        var unitOfWork = Substitute.For<IRaceMonitoringUnitOfWork>();
        var scopes = Substitute.For<ISosAlertScopeQuery>();
        var access = Substitute.For<IEventOrganizerAccessQuery>();
        var sosOps = Substitute.For<IEventSosOpsAccessQuery>();
        ICurrentUserAccessor current = new TestUser(UserId, admin: false);
        responses.GetByIdAsync(response.Id, Arg.Any<CancellationToken>()).Returns(response);
        scopes.GetAsync(response.SourceSosAlertId, Arg.Any<CancellationToken>()).Returns(scope);
        if (scope.EventId is Guid eventId)
        {
            access.GetAsync(eventId, Arg.Any<CancellationToken>())
                .Returns(new EventOrganizerAccessInfo(eventId, UserId, "RegistrationOpen", "Demo"));
            sosOps.CanManageSosAsync(eventId, UserId, Arg.Any<CancellationToken>()).Returns(allowed);
        }

        return new CoordinationContext(responses, resources, assignments, unitOfWork, scopes, access, sosOps, current);
    }

    private sealed record CoordinationContext(
        IEmergencyResponseRepository Responses,
        IEmergencyResourceRepository Resources,
        IEmergencyAssignmentRepository Assignments,
        IRaceMonitoringUnitOfWork UnitOfWork,
        ISosAlertScopeQuery Scopes,
        IEventOrganizerAccessQuery Access,
        IEventSosOpsAccessQuery SosOps,
        ICurrentUserAccessor Current);

    private sealed class TestUser(Guid? userId, bool admin) : ICurrentUserAccessor
    {
        public Guid? UserId { get; } = userId;

        public bool IsInRole(string role) => admin && role == "Admin";
    }
}
