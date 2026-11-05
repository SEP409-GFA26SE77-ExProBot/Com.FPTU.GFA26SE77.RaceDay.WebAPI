using System.Reflection;
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
    private static readonly Guid OtherOrganizerId = Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee");
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

    [Theory]
    [InlineData(EmergencyResponseStatus.Assigned)]
    [InlineData(EmergencyResponseStatus.InProgress)]
    public async Task Create_Assigns_Resource_Without_Changing_Response_Resource_Or_Sos(
        EmergencyResponseStatus status)
    {
        var response = ResponseWithStatus(status);
        var sourceSosAlertId = response.SourceSosAlertId;
        var assignedAt = response.AssignedAtUtc;
        var resource = Resource(EventId, EmergencyResourceStatus.Assigned);
        var scope = Scope(response);
        var ctx = AssignmentAccess(response, scope);
        ctx.Resources.GetByIdAsync(resource.Id, Arg.Any<CancellationToken>()).Returns(resource);
        ctx.Assignments.ExistsForResponseAsync(response.Id, resource.Id, Arg.Any<CancellationToken>()).Returns(false);

        var result = await CreateHandler(ctx).Handle(
            new CreateEmergencyAssignmentCommand(response.Id, new CreateEmergencyAssignmentRequest(resource.Id, null)),
            CancellationToken.None);

        Assert.Equal(EmergencyAssignmentStatus.Assigned.ToString(), result.Status);
        Assert.Equal(status, response.Status);
        Assert.Equal(assignedAt, response.AssignedAtUtc);
        Assert.Equal(EmergencyResourceStatus.Assigned, resource.Status);
        Assert.Equal(sourceSosAlertId, response.SourceSosAlertId);
        Assert.Equal(SourceSosStatus, scope.Status);
        ctx.Assignments.Received(1).Add(Arg.Is<EmergencyAssignment>(row =>
            row.EmergencyResponseId == response.Id && row.Status == EmergencyAssignmentStatus.Assigned));
        await ctx.UnitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        await ctx.Scopes.Received(1).GetAsync(sourceSosAlertId, Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(EmergencyResponseStatus.Resolved)]
    [InlineData(EmergencyResponseStatus.Cancelled)]
    public async Task Create_Rejects_Terminal_Response(EmergencyResponseStatus status)
    {
        var response = ResponseWithStatus(status);
        var ctx = AssignmentAccess(response, Scope(response));

        var ex = await Assert.ThrowsAsync<DomainException>(() => CreateHandler(ctx).Handle(
            new CreateEmergencyAssignmentCommand(response.Id, new CreateEmergencyAssignmentRequest(Guid.NewGuid(), null)),
            CancellationToken.None));

        Assert.Equal("Chỉ hồ sơ đang mở, đã xác nhận hoặc đang xử lý mới được phân công nguồn lực.", ex.Message);
        Assert.Equal(status, response.Status);
        await ctx.Resources.DidNotReceive().GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        ctx.Assignments.DidNotReceive().Add(Arg.Any<EmergencyAssignment>());
        await ctx.UnitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
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
    public async Task Cancel_Cancels_Unfinished_Assignments_And_Leaves_Finished_Rows_And_Source_Sos()
    {
        var response = OpenResponse();
        var sourceSosAlertId = response.SourceSosAlertId;
        var scope = Scope(response);
        var earlier = new DateTime(2026, 10, 27, 1, 0, 0, DateTimeKind.Utc);
        var acceptedAt = earlier.AddMinutes(5);
        var arrivedAt = earlier.AddMinutes(10);
        var completedAt = earlier.AddMinutes(15);
        var assigned = EmergencyAssignment.Create(
            response.Id, Guid.NewGuid(), earlier, EmergencyAssignmentStatus.Assigned);
        var accepted = EmergencyAssignment.Create(
            response.Id, Guid.NewGuid(), earlier, EmergencyAssignmentStatus.Accepted, acceptedAt);
        var arrived = EmergencyAssignment.Create(
            response.Id, Guid.NewGuid(), earlier, EmergencyAssignmentStatus.Arrived, acceptedAt, arrivedAt);
        var completed = EmergencyAssignment.Create(
            response.Id,
            Guid.NewGuid(),
            earlier,
            EmergencyAssignmentStatus.Completed,
            acceptedAt,
            arrivedAt,
            completedAt);
        var alreadyCancelled = EmergencyAssignment.Create(
            response.Id, Guid.NewGuid(), earlier, EmergencyAssignmentStatus.Cancelled, acceptedAt);
        var ctx = AssignmentAccess(response, scope);
        ctx.Assignments.ListTrackedByResponseAsync(response.Id, Arg.Any<CancellationToken>())
            .Returns(new List<EmergencyAssignment> { assigned, accepted, arrived, completed, alreadyCancelled });

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
        Assert.Equal(EmergencyAssignmentStatus.Cancelled, assigned.Status);
        Assert.Null(assigned.CompletedAtUtc);
        Assert.Equal(EmergencyAssignmentStatus.Cancelled, accepted.Status);
        Assert.Equal(acceptedAt, accepted.AcceptedAtUtc);
        Assert.Null(accepted.CompletedAtUtc);
        Assert.Equal(EmergencyAssignmentStatus.Cancelled, arrived.Status);
        Assert.Equal(arrivedAt, arrived.ArrivedAtUtc);
        Assert.Null(arrived.CompletedAtUtc);
        Assert.Equal(EmergencyAssignmentStatus.Completed, completed.Status);
        Assert.Equal(completedAt, completed.CompletedAtUtc);
        Assert.Equal(acceptedAt, completed.AcceptedAtUtc);
        Assert.Equal(EmergencyAssignmentStatus.Cancelled, alreadyCancelled.Status);
        Assert.Equal(acceptedAt, alreadyCancelled.AcceptedAtUtc);
        Assert.Null(alreadyCancelled.CompletedAtUtc);
        await ctx.Scopes.Received(1).GetAsync(sourceSosAlertId, Arg.Any<CancellationToken>());
        await ctx.Scopes.DidNotReceive().ListByEventAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
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
        var assignment = EmergencyAssignment.Create(
            response.Id, Guid.NewGuid(), DateTime.UtcNow, EmergencyAssignmentStatus.Assigned);
        var ctx = AssignmentAccess(response, Scope(response));
        ctx.Assignments.ListTrackedByResponseAsync(response.Id, Arg.Any<CancellationToken>())
            .Returns(new List<EmergencyAssignment> { assignment });

        var ex = await Assert.ThrowsAsync<DomainException>(() =>
            new CancelEmergencyResponseCommandHandler(
                    ctx.Responses, ctx.Assignments, ctx.UnitOfWork, ctx.Scopes, ctx.Access, ctx.SosOps, ctx.Current)
                .Handle(
                    new CancelEmergencyResponseCommand(response.Id, new CancelEmergencyResponseRequest("Muộn")),
                    CancellationToken.None));

        Assert.Equal("Phản hồi khẩn cấp không thể hủy ở trạng thái hiện tại.", ex.Message);
        Assert.Equal(EmergencyResponseStatus.Resolved, response.Status);
        Assert.Equal(EmergencyAssignmentStatus.Assigned, assignment.Status);
        Assert.Null(assignment.CompletedAtUtc);
        await ctx.Assignments.DidNotReceive().ListTrackedByResponseAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        await ctx.UnitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(EmergencyResponseStatus.Resolved)]
    [InlineData(EmergencyResponseStatus.Cancelled)]
    public async Task Update_Rejects_Assignment_When_Response_Is_Terminal(EmergencyResponseStatus status)
    {
        var response = status == EmergencyResponseStatus.Cancelled
            ? CancelledResponse()
            : EmergencyResponse.Create(
                Guid.NewGuid(),
                EmergencyIncidentType.Medical,
                EmergencyPriority.High,
                EmergencyResponseStatus.Resolved,
                DateTime.UtcNow,
                DateTime.UtcNow,
                "Đã xong.");
        var assignment = EmergencyAssignment.Create(
            response.Id, Guid.NewGuid(), DateTime.UtcNow, EmergencyAssignmentStatus.Arrived);
        var ctx = AssignmentAccess(response, Scope(response));
        ctx.Assignments.GetByIdAsync(assignment.Id, Arg.Any<CancellationToken>()).Returns(assignment);

        var ex = await Assert.ThrowsAsync<DomainException>(() =>
            new UpdateEmergencyAssignmentCommandHandler(
                    ctx.Responses, ctx.Resources, ctx.Assignments, ctx.UnitOfWork, ctx.Scopes, ctx.Access, ctx.SosOps, ctx.Current)
                .Handle(
                    new UpdateEmergencyAssignmentCommand(assignment.Id, new UpdateEmergencyAssignmentRequest("Completed")),
                    CancellationToken.None));

        Assert.Equal("Không thể cập nhật phân công khi hồ sơ đã kết thúc.", ex.Message);
        Assert.Equal(EmergencyAssignmentStatus.Arrived, assignment.Status);
        await ctx.Resources.DidNotReceive().GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        await ctx.UnitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(EmergencyResponseStatus.Assigned)]
    [InlineData(EmergencyResponseStatus.InProgress)]
    [InlineData(EmergencyResponseStatus.Resolved)]
    [InlineData(EmergencyResponseStatus.Cancelled)]
    public async Task Remove_Rejects_When_Response_Is_Not_Open(EmergencyResponseStatus status)
    {
        var response = ResponseWithStatus(status);
        var assignment = EmergencyAssignment.Create(
            response.Id, Guid.NewGuid(), DateTime.UtcNow, EmergencyAssignmentStatus.Assigned);
        var scope = Scope(response);
        var ctx = AssignmentAccess(response, scope);
        ctx.Assignments.GetByIdAsync(assignment.Id, Arg.Any<CancellationToken>()).Returns(assignment);

        var ex = await Assert.ThrowsAsync<DomainException>(() =>
            new RemoveEmergencyAssignmentCommandHandler(
                    ctx.Responses, ctx.Assignments, ctx.UnitOfWork, ctx.Scopes, ctx.Access, ctx.SosOps, ctx.Current)
                .Handle(new RemoveEmergencyAssignmentCommand(response.Id, assignment.Id), CancellationToken.None));

        Assert.Equal("Chỉ hồ sơ đang mở mới gỡ được phân công.", ex.Message);
        Assert.Equal(status, response.Status);
        Assert.Equal(SourceSosStatus, scope.Status);
        ctx.Assignments.DidNotReceive().Remove(Arg.Any<EmergencyAssignment>());
        await ctx.UnitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreateResource_Stores_Trimmed_Code_When_Event_Contract_Allows_Non_Owner()
    {
        var (resources, access, sosOps, current, unitOfWork) = ResourceWriter(ResourceWriterCase.AllowedCoordinator);
        var eventAccess = await access.GetAsync(EventId, CancellationToken.None);
        Assert.NotEqual(UserId, eventAccess!.OrganizerUserId);
        Assert.False(current.IsInRole("Admin"));
        resources.ExistsByEventAndCodeAsync(EventId, "MED-1", Arg.Any<CancellationToken>()).Returns(false);
        EmergencyResource? added = null;
        resources.When(x => x.Add(Arg.Any<EmergencyResource>()))
            .Do(call => added = call.Arg<EmergencyResource>());

        var result = await CreateResourceHandler(resources, access, sosOps, current, unitOfWork).Handle(
            new CreateEmergencyResourceCommand(EventId, new CreateEmergencyResourceRequest(
                "  MED-1  ", "ambulance", "  Xe 1  ", "Available", "  Ca ngày  ")),
            CancellationToken.None);

        Assert.NotNull(added);
        Assert.Equal(EventId, added.EventId);
        Assert.Equal("MED-1", added.ResourceCode);
        Assert.Equal(EmergencyResourceType.Ambulance, added.ResourceType);
        Assert.Equal("Xe 1", added.Name);
        Assert.Equal(EmergencyResourceStatus.Available, added.Status);
        Assert.Equal("Ca ngày", added.Notes);
        Assert.Equal(added.Id, result.Id);
        Assert.Equal("MED-1", result.ResourceCode);
        await resources.Received(1).ExistsByEventAndCodeAsync(EventId, "MED-1", Arg.Any<CancellationToken>());
        await sosOps.Received(1).CanManageSosAsync(EventId, UserId, Arg.Any<CancellationToken>());
        await unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreateResource_Allows_The_Same_Code_On_Another_Event()
    {
        var (resources, access, sosOps, current, unitOfWork) = ResourceWriter();
        access.GetAsync(OtherEventId, Arg.Any<CancellationToken>())
            .Returns(new EventOrganizerAccessInfo(OtherEventId, UserId, "RegistrationOpen", "Khác"));
        sosOps.CanManageSosAsync(OtherEventId, UserId, Arg.Any<CancellationToken>()).Returns(true);
        resources.ExistsByEventAndCodeAsync(OtherEventId, "MED-1", Arg.Any<CancellationToken>()).Returns(false);

        var result = await CreateResourceHandler(resources, access, sosOps, current, unitOfWork).Handle(
            new CreateEmergencyResourceCommand(OtherEventId, new CreateEmergencyResourceRequest(
                "MED-1", "MedicalTeam", "Đội y tế", "Assigned", null)),
            CancellationToken.None);

        Assert.Equal(OtherEventId, result.EventId);
        Assert.Equal("MED-1", result.ResourceCode);
        Assert.Equal("Assigned", result.Status);
        await resources.Received(1).ExistsByEventAndCodeAsync(OtherEventId, "MED-1", Arg.Any<CancellationToken>());
        await resources.DidNotReceive().ExistsByEventAndCodeAsync(EventId, Arg.Any<string>(), Arg.Any<CancellationToken>());
        resources.Received(1).Add(Arg.Is<EmergencyResource>(row => row.EventId == OtherEventId && row.ResourceCode == "MED-1"));
    }

    [Fact]
    public async Task CreateResource_Rejects_Duplicate_Code_In_The_Same_Event()
    {
        var (resources, access, sosOps, current, unitOfWork) = ResourceWriter();
        resources.ExistsByEventAndCodeAsync(EventId, "MED-1", Arg.Any<CancellationToken>()).Returns(true);

        var ex = await Assert.ThrowsAsync<DomainException>(() =>
            CreateResourceHandler(resources, access, sosOps, current, unitOfWork).Handle(
                new CreateEmergencyResourceCommand(EventId, new CreateEmergencyResourceRequest(
                    "MED-1", "MedicalTeam", "Đội y tế", "Available", null)),
                CancellationToken.None));

        Assert.Equal("Mã nguồn lực đã tồn tại trong giải.", ex.Message);
        resources.DidNotReceive().Add(Arg.Any<EmergencyResource>());
        await unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreateResource_Propagates_Unique_Conflict_From_Save()
    {
        var (resources, access, sosOps, current, unitOfWork) = ResourceWriter();
        resources.ExistsByEventAndCodeAsync(EventId, "MED-1", Arg.Any<CancellationToken>()).Returns(false);
        unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns<int>(_ => throw new DomainException("Mã nguồn lực đã tồn tại trong giải."));

        var ex = await Assert.ThrowsAsync<DomainException>(() =>
            CreateResourceHandler(resources, access, sosOps, current, unitOfWork).Handle(
                new CreateEmergencyResourceCommand(EventId, new CreateEmergencyResourceRequest(
                    "MED-1", "MedicalTeam", "Đội y tế", "Available", null)),
                CancellationToken.None));

        Assert.Equal("Mã nguồn lực đã tồn tại trong giải.", ex.Message);
        resources.Received(1).Add(Arg.Any<EmergencyResource>());
    }

    [Fact]
    public async Task CreateResource_Allows_Event_Owner()
    {
        var (resources, access, sosOps, current, unitOfWork) = ResourceWriter(ResourceWriterCase.Owner);
        var eventAccess = await access.GetAsync(EventId, CancellationToken.None);
        Assert.Equal(UserId, eventAccess!.OrganizerUserId);
        Assert.False(current.IsInRole("Admin"));
        resources.ExistsByEventAndCodeAsync(EventId, "MED-1", Arg.Any<CancellationToken>()).Returns(false);

        var result = await CreateResourceHandler(resources, access, sosOps, current, unitOfWork).Handle(
            new CreateEmergencyResourceCommand(EventId, new CreateEmergencyResourceRequest(
                "MED-1", "MedicalTeam", "Đội y tế", "Available", null)),
            CancellationToken.None);

        Assert.Equal(EventId, result.EventId);
        Assert.Equal("MED-1", result.ResourceCode);
        await sosOps.Received(1).CanManageSosAsync(EventId, UserId, Arg.Any<CancellationToken>());
        await unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreateResource_Rejects_Caller_Outside_The_Event()
    {
        var (resources, access, sosOps, current, unitOfWork) = ResourceWriter(ResourceWriterCase.Outsider);
        var eventAccess = await access.GetAsync(EventId, CancellationToken.None);
        Assert.NotEqual(UserId, eventAccess!.OrganizerUserId);
        Assert.False(current.IsInRole("Admin"));

        var ex = await Assert.ThrowsAsync<DomainException>(() =>
            CreateResourceHandler(resources, access, sosOps, current, unitOfWork).Handle(
                new CreateEmergencyResourceCommand(EventId, new CreateEmergencyResourceRequest(
                    "MED-1", "MedicalTeam", "Đội y tế", "Available", null)),
                CancellationToken.None));

        Assert.Equal("Chỉ organizer của giải hoặc Coordinator Active mới điều phối nguồn lực.", ex.Message);
        await resources.DidNotReceive().ExistsByEventAndCodeAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        resources.DidNotReceive().Add(Arg.Any<EmergencyResource>());
        await unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreateResource_Rejects_Admin_When_Event_Contract_Denies()
    {
        var (resources, access, sosOps, current, unitOfWork) = ResourceWriter(ResourceWriterCase.Admin);
        var eventAccess = await access.GetAsync(EventId, CancellationToken.None);
        Assert.NotEqual(UserId, eventAccess!.OrganizerUserId);
        Assert.True(current.IsInRole("Admin"));

        var ex = await Assert.ThrowsAsync<DomainException>(() =>
            CreateResourceHandler(resources, access, sosOps, current, unitOfWork).Handle(
                new CreateEmergencyResourceCommand(EventId, new CreateEmergencyResourceRequest(
                    "MED-1", "MedicalTeam", "Đội y tế", "Available", null)),
                CancellationToken.None));

        Assert.Equal("Chỉ organizer của giải hoặc Coordinator Active mới điều phối nguồn lực.", ex.Message);
        await resources.DidNotReceive().ExistsByEventAndCodeAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        resources.DidNotReceive().Add(Arg.Any<EmergencyResource>());
        await unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public void CreateResource_Validator_Rejects_Invalid_Input()
    {
        var validator = new CreateEmergencyResourceCommandValidator();
        var missing = validator.Validate(new CreateEmergencyResourceCommand(EventId, new CreateEmergencyResourceRequest(
            "  ", "Team", "", "Busy", null)));
        var tooLong = validator.Validate(new CreateEmergencyResourceCommand(EventId, new CreateEmergencyResourceRequest(
            new string('A', 51), "MedicalTeam", new string('B', 201), "Available", null)));

        Assert.False(missing.IsValid);
        Assert.Contains(missing.Errors, error => error.ErrorMessage == "Mã nguồn lực không được để trống.");
        Assert.Contains(missing.Errors, error => error.ErrorMessage == "Tên nguồn lực không được để trống.");
        Assert.Contains(missing.Errors, error => error.ErrorMessage == "Loại nguồn lực không hợp lệ.");
        Assert.Contains(missing.Errors, error => error.ErrorMessage == "Trạng thái nguồn lực không hợp lệ.");
        Assert.False(tooLong.IsValid);
        Assert.Contains(tooLong.Errors, error => error.ErrorMessage == "Mã nguồn lực tối đa 50 ký tự.");
        Assert.Contains(tooLong.Errors, error => error.ErrorMessage == "Tên nguồn lực tối đa 200 ký tự.");
    }

    [Fact]
    public void UpdateResource_Request_Does_Not_Expose_Identity_Or_Status()
    {
        var names = typeof(UpdateEmergencyResourceRequest).GetProperties().Select(property => property.Name).ToArray();

        Assert.Equal(["ResourceType", "Name", "Notes"], names);
    }

    [Fact]
    public async Task UpdateResource_Changes_Description_And_Keeps_Identity()
    {
        var resource = Resource(EventId, EmergencyResourceStatus.Offline);
        var (resources, access, sosOps, current, unitOfWork) = ResourceWriter(ResourceWriterCase.Owner);
        var eventAccess = await access.GetAsync(EventId, CancellationToken.None);
        Assert.Equal(UserId, eventAccess!.OrganizerUserId);
        Assert.False(current.IsInRole("Admin"));
        resources.GetTrackedByIdAsync(resource.Id, Arg.Any<CancellationToken>()).Returns(resource);

        var result = await UpdateResourceHandler(resources, access, sosOps, current, unitOfWork).Handle(
            new UpdateEmergencyResourceCommand(EventId, resource.Id, new UpdateEmergencyResourceRequest(
                "FirstAid", "  Đội sơ cứu  ", "   ")),
            CancellationToken.None);

        Assert.Equal(EventId, resource.EventId);
        Assert.Equal("MED-1", resource.ResourceCode);
        Assert.Equal(EmergencyResourceStatus.Offline, resource.Status);
        Assert.Equal(EmergencyResourceType.FirstAid, resource.ResourceType);
        Assert.Equal("Đội sơ cứu", resource.Name);
        Assert.Null(resource.Notes);
        Assert.Equal("Offline", result.Status);
        Assert.Equal("MED-1", result.ResourceCode);
        await resources.DidNotReceive().GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        await unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UpdateResource_Rejects_A_Different_Route_Event()
    {
        var resource = Resource(EventId, EmergencyResourceStatus.Available);
        var (resources, access, sosOps, current, unitOfWork) = ResourceWriter();
        resources.GetTrackedByIdAsync(resource.Id, Arg.Any<CancellationToken>()).Returns(resource);
        access.GetAsync(OtherEventId, Arg.Any<CancellationToken>())
            .Returns(new EventOrganizerAccessInfo(OtherEventId, UserId, "RegistrationOpen", "Khác"));

        var ex = await Assert.ThrowsAsync<DomainException>(() =>
            UpdateResourceHandler(resources, access, sosOps, current, unitOfWork).Handle(
                new UpdateEmergencyResourceCommand(OtherEventId, resource.Id, new UpdateEmergencyResourceRequest(
                    "Ambulance", "Xe", null)),
                CancellationToken.None));

        Assert.Equal("Nguồn lực không thuộc giải này.", ex.Message);
        Assert.Equal("MED-1", resource.ResourceCode);
        Assert.Equal(EmergencyResourceType.MedicalTeam, resource.ResourceType);
        await sosOps.Received(1).CanManageSosAsync(EventId, UserId, Arg.Any<CancellationToken>());
        await unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UpdateResource_Rejects_Missing_Resource()
    {
        var (resources, access, sosOps, current, unitOfWork) = ResourceWriter();

        var ex = await Assert.ThrowsAsync<DomainException>(() =>
            UpdateResourceHandler(resources, access, sosOps, current, unitOfWork).Handle(
                new UpdateEmergencyResourceCommand(EventId, Guid.NewGuid(), new UpdateEmergencyResourceRequest(
                    "Ambulance", "Xe", null)),
                CancellationToken.None));

        Assert.Equal("Không tìm thấy nguồn lực.", ex.Message);
        await sosOps.DidNotReceive().CanManageSosAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UpdateResource_Allows_Non_Owner_When_Event_Contract_Allows()
    {
        var resource = Resource(EventId, EmergencyResourceStatus.Offline);
        var (resources, access, sosOps, current, unitOfWork) = ResourceWriter(ResourceWriterCase.AllowedCoordinator);
        var eventAccess = await access.GetAsync(EventId, CancellationToken.None);
        Assert.NotEqual(UserId, eventAccess!.OrganizerUserId);
        Assert.False(current.IsInRole("Admin"));
        resources.GetTrackedByIdAsync(resource.Id, Arg.Any<CancellationToken>()).Returns(resource);

        var result = await UpdateResourceHandler(resources, access, sosOps, current, unitOfWork).Handle(
            new UpdateEmergencyResourceCommand(EventId, resource.Id, new UpdateEmergencyResourceRequest(
                "Ambulance", "Xe cứu thương", null)),
            CancellationToken.None);

        Assert.Equal(EventId, resource.EventId);
        Assert.Equal("MED-1", resource.ResourceCode);
        Assert.Equal(EmergencyResourceStatus.Offline, resource.Status);
        Assert.Equal(EmergencyResourceType.Ambulance, resource.ResourceType);
        Assert.Equal("Ambulance", result.ResourceType);
        await sosOps.Received(1).CanManageSosAsync(EventId, UserId, Arg.Any<CancellationToken>());
        await unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UpdateResource_Rejects_Caller_Outside_The_Event()
    {
        var resource = Resource(EventId, EmergencyResourceStatus.Available);
        var (resources, access, sosOps, current, unitOfWork) = ResourceWriter(ResourceWriterCase.Outsider);
        var eventAccess = await access.GetAsync(EventId, CancellationToken.None);
        Assert.NotEqual(UserId, eventAccess!.OrganizerUserId);
        Assert.False(current.IsInRole("Admin"));
        resources.GetTrackedByIdAsync(resource.Id, Arg.Any<CancellationToken>()).Returns(resource);

        var ex = await Assert.ThrowsAsync<DomainException>(() =>
            UpdateResourceHandler(resources, access, sosOps, current, unitOfWork).Handle(
                new UpdateEmergencyResourceCommand(EventId, resource.Id, new UpdateEmergencyResourceRequest(
                    "Ambulance", "Xe", null)),
                CancellationToken.None));

        Assert.Equal("Chỉ organizer của giải hoặc Coordinator Active mới điều phối nguồn lực.", ex.Message);
        Assert.Equal(EmergencyResourceType.MedicalTeam, resource.ResourceType);
        Assert.Equal("MED-1", resource.ResourceCode);
        await unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UpdateResource_Rejects_Admin_When_Event_Contract_Denies()
    {
        var resource = Resource(EventId, EmergencyResourceStatus.Available);
        var (resources, access, sosOps, current, unitOfWork) = ResourceWriter(ResourceWriterCase.Admin);
        var eventAccess = await access.GetAsync(EventId, CancellationToken.None);
        Assert.NotEqual(UserId, eventAccess!.OrganizerUserId);
        Assert.True(current.IsInRole("Admin"));
        resources.GetTrackedByIdAsync(resource.Id, Arg.Any<CancellationToken>()).Returns(resource);

        var ex = await Assert.ThrowsAsync<DomainException>(() =>
            UpdateResourceHandler(resources, access, sosOps, current, unitOfWork).Handle(
                new UpdateEmergencyResourceCommand(EventId, resource.Id, new UpdateEmergencyResourceRequest(
                    "Ambulance", "Xe", null)),
                CancellationToken.None));

        Assert.Equal("Chỉ organizer của giải hoặc Coordinator Active mới điều phối nguồn lực.", ex.Message);
        Assert.Equal(EmergencyResourceType.MedicalTeam, resource.ResourceType);
        Assert.Equal("MED-1", resource.ResourceCode);
        await unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
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
        ResponseWithStatus(EmergencyResponseStatus.Open);

    private static EmergencyResponse ResponseWithStatus(EmergencyResponseStatus status)
    {
        var active = status is EmergencyResponseStatus.Assigned
            or EmergencyResponseStatus.InProgress
            or EmergencyResponseStatus.Resolved;
        return EmergencyResponse.Create(
            Guid.NewGuid(),
            EmergencyIncidentType.Medical,
            EmergencyPriority.High,
            status,
            active ? new DateTime(2026, 10, 27, 1, 0, 0, DateTimeKind.Utc) : null,
            status == EmergencyResponseStatus.Resolved
                ? new DateTime(2026, 10, 27, 2, 0, 0, DateTimeKind.Utc)
                : null,
            status == EmergencyResponseStatus.Resolved ? "Đã xong." : null);
    }

    private static EmergencyResponse CancelledResponse()
    {
        var response = OpenResponse();
        response.Cancel("Đã hủy.", DateTime.UtcNow);
        return response;
    }

    private static CreateEmergencyResourceCommandHandler CreateResourceHandler(
        IEmergencyResourceRepository resources,
        IEventOrganizerAccessQuery access,
        IEventSosOpsAccessQuery sosOps,
        ICurrentUserAccessor current,
        IRaceMonitoringUnitOfWork unitOfWork) =>
        new(resources, unitOfWork, access, sosOps, current);

    private static UpdateEmergencyResourceCommandHandler UpdateResourceHandler(
        IEmergencyResourceRepository resources,
        IEventOrganizerAccessQuery access,
        IEventSosOpsAccessQuery sosOps,
        ICurrentUserAccessor current,
        IRaceMonitoringUnitOfWork unitOfWork) =>
        new(resources, unitOfWork, access, sosOps, current);

    private static (
        IEmergencyResourceRepository Resources,
        IEventOrganizerAccessQuery Access,
        IEventSosOpsAccessQuery SosOps,
        ICurrentUserAccessor Current,
        IRaceMonitoringUnitOfWork UnitOfWork) ResourceWriter(ResourceWriterCase writerCase = ResourceWriterCase.Owner)
    {
        var allowed = writerCase is ResourceWriterCase.Owner or ResourceWriterCase.AllowedCoordinator;
        var (resources, access, sosOps, current) = ResourceAccess(allowed, writerCase == ResourceWriterCase.Admin);
        if (writerCase != ResourceWriterCase.Owner)
        {
            access.GetAsync(EventId, Arg.Any<CancellationToken>())
                .Returns(new EventOrganizerAccessInfo(EventId, OtherOrganizerId, "RegistrationOpen", "Demo"));
        }

        var unitOfWork = Substitute.For<IRaceMonitoringUnitOfWork>();
        return (resources, access, sosOps, current, unitOfWork);
    }

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

    private enum ResourceWriterCase
    {
        Owner,
        AllowedCoordinator,
        Outsider,
        Admin
    }

    private sealed class TestUser(Guid? userId, bool admin) : ICurrentUserAccessor
    {
        public Guid? UserId { get; } = userId;

        public bool IsInRole(string role) => admin && role == "Admin";
    }
}
