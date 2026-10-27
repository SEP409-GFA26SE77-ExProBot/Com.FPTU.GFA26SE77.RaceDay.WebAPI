using NSubstitute;
using RaceDay.Modules.Device.Contracts.Sos;
using RaceDay.Modules.Event.Contracts.Events;
using RaceDay.Modules.RaceMonitoring.Application.Abstractions;
using RaceDay.Modules.RaceMonitoring.Application.EmergencyResponses;
using RaceDay.Modules.RaceMonitoring.Domain;
using RaceDay.Modules.RaceMonitoring.Domain.EmergencyCoordination;
using RaceDay.Shared.Application.Abstractions;
using RaceDay.Shared.Domain.Exceptions;

namespace RaceDay.Modules.RaceMonitoring.UnitTests.Application;

public class EmergencyResponseHandlerTests
{
    private static readonly Guid UserId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid EventId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
    private static readonly DateTime AssignedAt = new(2026, 10, 27, 1, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task List_Returns_Authorized_Responses_Without_Changing_Source_Sos_Status()
    {
        var sourceSosAlertId = Guid.NewGuid();
        var response = OpenResponse(sourceSosAlertId);
        var scope = new SosAlertScope(sourceSosAlertId, EventId, "Acknowledged");
        var (responses, scopes, access, sosOps, current) = Access();
        responses.ListBySourceSosIdsAsync(
                Arg.Any<IReadOnlyCollection<Guid>>(),
                EmergencyResponseStatus.Open,
                null,
                null,
                1,
                20,
                Arg.Any<CancellationToken>())
            .Returns(([response], 1));
        scopes.ListByEventAsync(EventId, Arg.Any<CancellationToken>()).Returns([scope]);

        var result = await new ListEmergencyResponsesQueryHandler(responses, scopes, access, sosOps, current)
            .Handle(new ListEmergencyResponsesQuery(EventId, "open", null, null), CancellationToken.None);

        var item = Assert.Single(result.Items);
        Assert.Equal(1, result.Total);
        Assert.Equal("Open", item.Status);
        Assert.Equal("Acknowledged", item.SourceSosStatus);
        Assert.Equal("Acknowledged", scope.Status);
        Assert.Equal(EventId, item.EventId);
        await sosOps.Received(1).CanManageSosAsync(EventId, UserId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task List_Rejects_Coordinator_Outside_Event()
    {
        var (responses, scopes, access, sosOps, current) = Access(allowed: false);

        var ex = await Assert.ThrowsAsync<DomainException>(() =>
            new ListEmergencyResponsesQueryHandler(responses, scopes, access, sosOps, current)
                .Handle(new ListEmergencyResponsesQuery(EventId, null, null, null), CancellationToken.None));

        Assert.Equal("Chỉ organizer của giải hoặc Coordinator Active mới xem phản hồi khẩn cấp.", ex.Message);
        await responses.DidNotReceive().ListBySourceSosIdsAsync(
            Arg.Any<IReadOnlyCollection<Guid>>(),
            Arg.Any<EmergencyResponseStatus?>(),
            Arg.Any<EmergencyPriority?>(),
            Arg.Any<EmergencyIncidentType?>(),
            Arg.Any<int>(),
            Arg.Any<int>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task List_Rejects_Missing_User()
    {
        var (responses, scopes, access, sosOps, current) = Access(anonymous: true);

        var ex = await Assert.ThrowsAsync<DomainException>(() =>
            new ListEmergencyResponsesQueryHandler(responses, scopes, access, sosOps, current)
                .Handle(new ListEmergencyResponsesQuery(EventId, null, null, null), CancellationToken.None));

        Assert.Equal("Cần đăng nhập.", ex.Message);
        await access.DidNotReceive().GetAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task List_Rejects_Unknown_Status_Filter()
    {
        var (responses, scopes, access, sosOps, current) = Access();

        var ex = await Assert.ThrowsAsync<DomainException>(() =>
            new ListEmergencyResponsesQueryHandler(responses, scopes, access, sosOps, current)
                .Handle(new ListEmergencyResponsesQuery(EventId, "closed", null, null), CancellationToken.None));

        Assert.Equal("Trạng thái phản hồi khẩn cấp không hợp lệ.", ex.Message);
        await scopes.DidNotReceive().ListByEventAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Get_Returns_Authorized_Response()
    {
        var response = OpenResponse(Guid.NewGuid());
        var (responses, scopes, access, sosOps, current, unitOfWork) = MutableAccess(response, "Open");

        var dto = await new GetEmergencyResponseQueryHandler(responses, scopes, access, sosOps, current)
            .Handle(new GetEmergencyResponseQuery(response.Id), CancellationToken.None);

        Assert.Equal(response.Id, dto.Id);
        Assert.Equal("Open", dto.Status);
        Assert.Equal("Open", dto.SourceSosStatus);
        await unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Get_Rejects_Missing_Response()
    {
        var (responses, scopes, access, sosOps, current) = Access();
        responses.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns((EmergencyResponse?)null);

        var ex = await Assert.ThrowsAsync<DomainException>(() =>
            new GetEmergencyResponseQueryHandler(responses, scopes, access, sosOps, current)
                .Handle(new GetEmergencyResponseQuery(Guid.NewGuid()), CancellationToken.None));

        Assert.Equal("Không tìm thấy phản hồi khẩn cấp.", ex.Message);
        await scopes.DidNotReceive().GetAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Get_Rejects_Out_Of_Scope_Coordinator()
    {
        var response = OpenResponse(Guid.NewGuid());
        var (responses, scopes, access, sosOps, current, _) = MutableAccess(response, "Open", allowed: false);

        var ex = await Assert.ThrowsAsync<DomainException>(() =>
            new GetEmergencyResponseQueryHandler(responses, scopes, access, sosOps, current)
                .Handle(new GetEmergencyResponseQuery(response.Id), CancellationToken.None));

        Assert.Equal("Chỉ organizer của giải hoặc Coordinator Active mới xem phản hồi khẩn cấp.", ex.Message);
    }

    [Fact]
    public async Task Get_Rejects_Missing_Source_Sos()
    {
        var response = OpenResponse(Guid.NewGuid());
        var (responses, scopes, access, sosOps, current) = Access();
        responses.GetByIdAsync(response.Id, Arg.Any<CancellationToken>()).Returns(response);
        scopes.GetAsync(response.SourceSosAlertId, Arg.Any<CancellationToken>()).Returns((SosAlertScope?)null);

        var ex = await Assert.ThrowsAsync<DomainException>(() =>
            new GetEmergencyResponseQueryHandler(responses, scopes, access, sosOps, current)
                .Handle(new GetEmergencyResponseQuery(response.Id), CancellationToken.None));

        Assert.Equal("Không tìm thấy SOS nguồn.", ex.Message);
    }

    [Fact]
    public async Task Get_Rejects_Sos_Without_Event_For_Non_Admin()
    {
        var response = OpenResponse(Guid.NewGuid());
        var (responses, scopes, access, sosOps, current, _) = MutableAccess(response, "Open", attachEvent: false);

        var ex = await Assert.ThrowsAsync<DomainException>(() =>
            new GetEmergencyResponseQueryHandler(responses, scopes, access, sosOps, current)
                .Handle(new GetEmergencyResponseQuery(response.Id), CancellationToken.None));

        Assert.Equal("SOS nguồn chưa gắn giải — chỉ Admin xử lý.", ex.Message);
        await access.DidNotReceive().GetAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Acknowledge_Updates_Response_And_Leaves_Source_Sos_Status()
    {
        var response = OpenResponse(Guid.NewGuid());
        var (responses, scopes, access, sosOps, current, unitOfWork) = MutableAccess(response, "Open");

        var dto = await new AcknowledgeEmergencyResponseCommandHandler(
                responses, unitOfWork, scopes, access, sosOps, current)
            .Handle(new AcknowledgeEmergencyResponseCommand(response.Id), CancellationToken.None);

        Assert.Equal("Assigned", dto.Status);
        Assert.Equal("Open", dto.SourceSosStatus);
        Assert.NotNull(dto.AssignedAtUtc);
        Assert.Equal(EmergencyResponseStatus.Assigned, response.Status);
        await unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        await scopes.Received(1).GetAsync(response.SourceSosAlertId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Acknowledge_Allows_Admin_When_Source_Sos_Has_No_Event()
    {
        var response = OpenResponse(Guid.NewGuid());
        var (responses, scopes, access, sosOps, current, unitOfWork) =
            MutableAccess(response, "Open", admin: true, attachEvent: false);

        var dto = await new AcknowledgeEmergencyResponseCommandHandler(
                responses, unitOfWork, scopes, access, sosOps, current)
            .Handle(new AcknowledgeEmergencyResponseCommand(response.Id), CancellationToken.None);

        Assert.Equal("Assigned", dto.Status);
        Assert.Null(dto.EventId);
        Assert.Equal("Open", dto.SourceSosStatus);
        await access.DidNotReceive().GetAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        await sosOps.DidNotReceive().CanManageSosAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Acknowledge_Rejects_Invalid_Transition_Without_Saving()
    {
        var response = AssignedResponse();
        var (responses, scopes, access, sosOps, current, unitOfWork) = MutableAccess(response, "Acknowledged");

        var ex = await Assert.ThrowsAsync<DomainException>(() =>
            new AcknowledgeEmergencyResponseCommandHandler(responses, unitOfWork, scopes, access, sosOps, current)
                .Handle(new AcknowledgeEmergencyResponseCommand(response.Id), CancellationToken.None));

        Assert.Equal("Chỉ phản hồi đang mở mới được xác nhận.", ex.Message);
        Assert.Equal(EmergencyResponseStatus.Assigned, response.Status);
        await unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Update_Moves_Assigned_Response_To_InProgress()
    {
        var response = AssignedResponse();
        var (responses, scopes, access, sosOps, current, unitOfWork) = MutableAccess(response, "Acknowledged");

        var dto = await new UpdateEmergencyResponseCommandHandler(
                responses, unitOfWork, scopes, access, sosOps, current)
            .Handle(
                new UpdateEmergencyResponseCommand(
                    response.Id,
                    new UpdateEmergencyResponseRequest("Critical", "InProgress")),
                CancellationToken.None);

        Assert.Equal("InProgress", dto.Status);
        Assert.Equal("Critical", dto.Priority);
        Assert.Equal("Acknowledged", dto.SourceSosStatus);
        Assert.Equal(AssignedAt, dto.AssignedAtUtc);
        await unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Resolve_From_Assigned_Sets_Metadata_And_Leaves_Source_Sos_Status()
    {
        var response = AssignedResponse();
        var (responses, scopes, access, sosOps, current, unitOfWork) = MutableAccess(response, "Open");

        var dto = await new ResolveEmergencyResponseCommandHandler(
                responses, unitOfWork, scopes, access, sosOps, current)
            .Handle(
                new ResolveEmergencyResponseCommand(
                    response.Id,
                    new ResolveEmergencyResponseRequest("  Đã hỗ trợ tại chỗ.  ")),
                CancellationToken.None);

        Assert.Equal("Resolved", dto.Status);
        Assert.Equal("Đã hỗ trợ tại chỗ.", dto.ResolutionNotes);
        Assert.NotNull(dto.ResolvedAtUtc);
        Assert.Equal("Open", dto.SourceSosStatus);
        Assert.Equal(EmergencyResponseStatus.Resolved, response.Status);
        await unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Resolve_From_Open_Is_Rejected_Without_Saving()
    {
        var response = OpenResponse(Guid.NewGuid());
        var (responses, scopes, access, sosOps, current, unitOfWork) = MutableAccess(response, "Open");

        var ex = await Assert.ThrowsAsync<DomainException>(() =>
            new ResolveEmergencyResponseCommandHandler(responses, unitOfWork, scopes, access, sosOps, current)
                .Handle(
                    new ResolveEmergencyResponseCommand(response.Id, new ResolveEmergencyResponseRequest("Xong.")),
                    CancellationToken.None));

        Assert.Equal("Phản hồi khẩn cấp không thể kết thúc ở trạng thái hiện tại.", ex.Message);
        Assert.Equal(EmergencyResponseStatus.Open, response.Status);
        Assert.Null(response.ResolvedAtUtc);
        await unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Resolve_Validator_Rejects_Blank_Notes()
    {
        var result = new ResolveEmergencyResponseCommandValidator().Validate(
            new ResolveEmergencyResponseCommand(Guid.NewGuid(), new ResolveEmergencyResponseRequest("   ")));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.ErrorMessage == "Nội dung kết thúc phản hồi không được để trống.");
    }

    [Fact]
    public void Update_Validator_Requires_A_Field_And_Rejects_Resolve_Status()
    {
        var empty = new UpdateEmergencyResponseCommandValidator().Validate(
            new UpdateEmergencyResponseCommand(Guid.NewGuid(), new UpdateEmergencyResponseRequest(null, null)));
        var resolve = new UpdateEmergencyResponseCommandValidator().Validate(
            new UpdateEmergencyResponseCommand(Guid.NewGuid(), new UpdateEmergencyResponseRequest(null, "Resolved")));

        Assert.False(empty.IsValid);
        Assert.False(resolve.IsValid);
        Assert.Contains(resolve.Errors, error => error.ErrorMessage == "Chỉ được chuyển tiến độ sang InProgress.");
    }

    private static EmergencyResponse OpenResponse(Guid sourceSosAlertId) =>
        EmergencyResponse.Create(
            sourceSosAlertId,
            EmergencyIncidentType.Medical,
            EmergencyPriority.High,
            EmergencyResponseStatus.Open);

    private static EmergencyResponse AssignedResponse() =>
        EmergencyResponse.Create(
            Guid.NewGuid(),
            EmergencyIncidentType.Medical,
            EmergencyPriority.High,
            EmergencyResponseStatus.Assigned,
            AssignedAt);

    private static (
        IEmergencyResponseRepository Responses,
        ISosAlertScopeQuery Scopes,
        IEventOrganizerAccessQuery Access,
        IEventSosOpsAccessQuery SosOps,
        ICurrentUserAccessor Current) Access(bool allowed = true, bool admin = false, bool anonymous = false)
    {
        var responses = Substitute.For<IEmergencyResponseRepository>();
        var scopes = Substitute.For<ISosAlertScopeQuery>();
        var access = Substitute.For<IEventOrganizerAccessQuery>();
        var sosOps = Substitute.For<IEventSosOpsAccessQuery>();
        ICurrentUserAccessor current = new TestUser(anonymous ? null : UserId, admin);
        access.GetAsync(EventId, Arg.Any<CancellationToken>())
            .Returns(new EventOrganizerAccessInfo(EventId, UserId, "RegistrationOpen", "Demo"));
        sosOps.CanManageSosAsync(EventId, UserId, Arg.Any<CancellationToken>()).Returns(allowed);
        return (responses, scopes, access, sosOps, current);
    }

    private static (
        IEmergencyResponseRepository Responses,
        ISosAlertScopeQuery Scopes,
        IEventOrganizerAccessQuery Access,
        IEventSosOpsAccessQuery SosOps,
        ICurrentUserAccessor Current,
        IRaceMonitoringUnitOfWork UnitOfWork) MutableAccess(
        EmergencyResponse response,
        string sourceStatus,
        bool allowed = true,
        bool admin = false,
        bool attachEvent = true)
    {
        var (responses, scopes, access, sosOps, current) = Access(allowed, admin);
        var unitOfWork = Substitute.For<IRaceMonitoringUnitOfWork>();
        Guid? scopeEventId = attachEvent ? EventId : null;
        responses.GetByIdAsync(response.Id, Arg.Any<CancellationToken>()).Returns(response);
        scopes.GetAsync(response.SourceSosAlertId, Arg.Any<CancellationToken>())
            .Returns(new SosAlertScope(response.SourceSosAlertId, scopeEventId, sourceStatus));
        return (responses, scopes, access, sosOps, current, unitOfWork);
    }

    private sealed class TestUser(Guid? userId, bool admin) : ICurrentUserAccessor
    {
        public Guid? UserId { get; } = userId;

        public bool IsInRole(string role) => admin && role == "Admin";
    }
}
