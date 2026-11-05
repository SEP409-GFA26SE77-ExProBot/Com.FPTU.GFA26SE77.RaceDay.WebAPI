using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RaceDay.Modules.RaceMonitoring.Application;
using RaceDay.Modules.RaceMonitoring.Application.CheckpointScans;
using RaceDay.Modules.RaceMonitoring.Application.EmergencyAssignments;
using RaceDay.Modules.RaceMonitoring.Application.EmergencyResources;
using RaceDay.Modules.RaceMonitoring.Application.EmergencyResponses;
using RaceDay.Modules.RaceMonitoring.Application.RaceEventLogs;
using RaceDay.Modules.RaceMonitoring.Application.Abstractions;
using RaceDay.Modules.RaceMonitoring.Infrastructure;
using RaceDay.Shared.Web.Auth;
using RaceDay.Shared.Web.OpenApi;

namespace RaceDay.Modules.RaceMonitoring.Api;

public static class RaceMonitoringModule
{
    public static IServiceCollection AddRaceMonitoringModule(this IServiceCollection services, IConfiguration configuration)
    {
        // MediatR đăng ký tập trung ở Host (Program.cs)
        services.AddValidatorsFromAssembly(typeof(RaceMonitoringApplicationMarker).Assembly);
        services.AddRaceMonitoringInfrastructure(configuration);
        return services;
    }

    public static IEndpointRouteBuilder MapRaceMonitoringEndpoints(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api/v1/race-monitoring")
            .WithTags(ApiTags.RaceMonitoring)
            .RequireRateLimiting("public");

        api.MapGet("/health", () => Results.Ok(new { module = "race-monitoring", status = "ready" }))
            .WithSummary("RaceMonitoring module health");

        var ops = api.MapGroup("")
            .RequireAuthorization(AuthPolicies.OrganizerOrCoordinator)
            .RequireRateLimiting("authenticated");

        ops.MapGet("/events/{eventId:guid}/emergency-responses", async (
            Guid eventId,
            string? status,
            string? priority,
            string? incidentType,
            int? page,
            int? pageSize,
            IMediator mediator,
            CancellationToken ct) =>
            Results.Ok(await mediator.Send(
                new ListEmergencyResponsesQuery(eventId, status, priority, incidentType, page ?? 1, pageSize ?? 20),
                ct)))
            .WithSummary("Danh sách phản hồi khẩn cấp theo giải")
            .WithDescription(
                "JWT role Admin | Organizer | Coordinator (không wear). "
                + "Handler: chủ giải hoặc staff Coordinator Active trên giải. "
                + "Lọc status, priority, incidentType. Phân trang page/pageSize.");

        ops.MapGet("/emergency-responses/{id:guid}", async (Guid id, IMediator mediator, CancellationToken ct) =>
            Results.Ok(await mediator.Send(new GetEmergencyResponseQuery(id), ct)))
            .WithSummary("Chi tiết phản hồi khẩn cấp")
            .WithDescription("Quyền trên giải của SOS nguồn. Không trả hồ sơ y tế runner.");

        ops.MapPost("/emergency-responses/{id:guid}/acknowledge", async (Guid id, IMediator mediator, CancellationToken ct) =>
            Results.Ok(await mediator.Send(new AcknowledgeEmergencyResponseCommand(id), ct)))
            .WithSummary("Xác nhận phản hồi khẩn cấp")
            .WithDescription("Open → Assigned. Không đổi trạng thái SOS thiết bị.");

        ops.MapPatch("/emergency-responses/{id:guid}", async (
            Guid id,
            UpdateEmergencyResponseRequest body,
            IMediator mediator,
            CancellationToken ct) =>
            Results.Ok(await mediator.Send(new UpdateEmergencyResponseCommand(id, body), ct)))
            .WithSummary("Cập nhật tiến độ phản hồi khẩn cấp")
            .WithDescription("Body: { priority?: Low|Medium|High|Critical, status?: InProgress }. Assigned → InProgress.");

        ops.MapPost("/emergency-responses/{id:guid}/resolve", async (
            Guid id,
            ResolveEmergencyResponseRequest body,
            IMediator mediator,
            CancellationToken ct) =>
            Results.Ok(await mediator.Send(new ResolveEmergencyResponseCommand(id, body), ct)))
            .WithSummary("Kết thúc phản hồi khẩn cấp")
            .WithDescription(
                "Chỉ từ Assigned hoặc InProgress. Body: { resolutionNotes }. "
                + "Bị từ chối nếu còn phân công chưa kết thúc (khác Completed và Cancelled). "
                + "Không tự hoàn thành hay hủy phân công. "
                + "Không gọi resolve của SOS thiết bị.");

        ops.MapPost("/emergency-responses/{id:guid}/cancel", async (
            Guid id,
            CancelEmergencyResponseRequest body,
            IMediator mediator,
            CancellationToken ct) =>
            Results.Ok(await mediator.Send(new CancelEmergencyResponseCommand(id, body), ct)))
            .WithSummary("Hủy phản hồi khẩn cấp")
            .WithDescription(
                "Từ Open, Assigned hoặc InProgress. Body: { reason }. "
                + "Phân công Assigned, Accepted hoặc Arrived chuyển sang Cancelled và không ghi CompletedAtUtc. "
                + "Phân công Completed hoặc Cancelled giữ nguyên. Không đổi SOS thiết bị. "
                + "Chỉ chủ giải hoặc Coordinator Active.");

        ops.MapGet("/events/{eventId:guid}/emergency-resources", async (
            Guid eventId,
            string? status,
            string? resourceType,
            int? page,
            int? pageSize,
            IMediator mediator,
            CancellationToken ct) =>
            Results.Ok(await mediator.Send(
                new ListEmergencyResourcesQuery(eventId, status, resourceType, page ?? 1, pageSize ?? 20),
                ct)))
            .WithSummary("Danh sách nguồn lực của giải")
            .WithDescription(
                "Chỉ nguồn lực có EventId trùng giải. "
                + "Chủ giải hoặc Coordinator Active. Admin không đi tắt. "
                + "Lọc status, resourceType. Phân trang page/pageSize.");

        ops.MapGet("/emergency-resources/{id:guid}", async (Guid id, IMediator mediator, CancellationToken ct) =>
            Results.Ok(await mediator.Send(new GetEmergencyResourceQuery(id), ct)))
            .WithSummary("Chi tiết nguồn lực")
            .WithDescription(
                "Nguồn lực phải thuộc một giải. "
                + "Chủ giải hoặc Coordinator Active. Admin không đi tắt.");

        ops.MapPost("/events/{eventId:guid}/emergency-resources", async (
            Guid eventId,
            CreateEmergencyResourceRequest body,
            IMediator mediator,
            CancellationToken ct) =>
            Results.Ok(await mediator.Send(new CreateEmergencyResourceCommand(eventId, body), ct)))
            .WithSummary("Tạo nguồn lực của giải")
            .WithDescription(
                "Body: { resourceCode, resourceType, name, status, notes? }. "
                + "resourceType: MedicalTeam | Ambulance | FirstAid | Other. "
                + "status: Available | Assigned | Unavailable | Offline. "
                + "Chủ giải hoặc Coordinator Active. Admin không đi tắt. "
                + "Mã trùng trong cùng giải bị từ chối. Cùng mã ở giải khác vẫn hợp lệ.");

        ops.MapPatch("/events/{eventId:guid}/emergency-resources/{id:guid}", async (
            Guid eventId,
            Guid id,
            UpdateEmergencyResourceRequest body,
            IMediator mediator,
            CancellationToken ct) =>
            Results.Ok(await mediator.Send(new UpdateEmergencyResourceCommand(eventId, id, body), ct)))
            .WithSummary("Cập nhật thông tin nguồn lực")
            .WithDescription(
                "Body: { resourceType, name, notes? }. "
                + "Không đổi EventId, ResourceCode hay Status. "
                + "notes rỗng sẽ xóa ghi chú. "
                + "Nguồn lực phải thuộc eventId trên route. "
                + "Chủ giải hoặc Coordinator Active của giải đó. Admin không đi tắt.");

        ops.MapGet("/emergency-responses/{responseId:guid}/assignments", async (
            Guid responseId,
            int? page,
            int? pageSize,
            IMediator mediator,
            CancellationToken ct) =>
            Results.Ok(await mediator.Send(
                new ListEmergencyAssignmentsQuery(responseId, page ?? 1, pageSize ?? 20),
                ct)))
            .WithSummary("Danh sách phân công của hồ sơ ứng cứu")
            .WithDescription(
                "Chỉ hồ sơ thuộc giải mà chủ giải hoặc Coordinator Active được điều phối. "
                + "status: Assigned | Accepted | Arrived | Completed | Cancelled.");

        ops.MapPost("/emergency-responses/{responseId:guid}/assignments", async (
            Guid responseId,
            CreateEmergencyAssignmentRequest body,
            IMediator mediator,
            CancellationToken ct) =>
            Results.Ok(await mediator.Send(new CreateEmergencyAssignmentCommand(responseId, body), ct)))
            .WithSummary("Phân công nguồn lực cho hồ sơ")
            .WithDescription(
                "Body: { emergencyResourceId, notes? }. "
                + "Hồ sơ phải Open, Assigned hoặc InProgress. "
                + "Không tự xác nhận hồ sơ và không đổi trạng thái hồ sơ. "
                + "Nguồn lực phải cùng giải và không Unavailable/Offline. "
                + "Không đổi trạng thái nguồn lực hay SOS thiết bị. "
                + "Một nguồn lực có thể nằm trên nhiều hồ sơ.");

        ops.MapDelete("/emergency-responses/{responseId:guid}/assignments/{assignmentId:guid}", async (
            Guid responseId,
            Guid assignmentId,
            IMediator mediator,
            CancellationToken ct) =>
        {
            await mediator.Send(new RemoveEmergencyAssignmentCommand(responseId, assignmentId), ct);
            return Results.NoContent();
        })
            .WithSummary("Gỡ phân công khỏi hồ sơ đang mở")
            .WithDescription("Xóa dòng phân công của đúng hồ sơ. Chỉ khi hồ sơ còn Open.");

        ops.MapGet("/emergency-assignments/{assignmentId:guid}", async (
            Guid assignmentId,
            IMediator mediator,
            CancellationToken ct) =>
            Results.Ok(await mediator.Send(new GetEmergencyAssignmentQuery(assignmentId), ct)))
            .WithSummary("Chi tiết một phân công")
            .WithDescription(
                "Quyền trên giải của hồ sơ cha. Không đổi SOS thiết bị. "
                + "status có thể là Cancelled khi hồ sơ cha bị hủy.");

        ops.MapPatch("/emergency-assignments/{assignmentId:guid}", async (
            Guid assignmentId,
            UpdateEmergencyAssignmentRequest body,
            IMediator mediator,
            CancellationToken ct) =>
            Results.Ok(await mediator.Send(new UpdateEmergencyAssignmentCommand(assignmentId, body), ct)))
            .WithSummary("Cập nhật tiến độ phân công")
            .WithDescription(
                "Body: { status: Accepted | Arrived | Completed }. "
                + "Mỗi bước ghi mốc thời gian. Assigned → Accepted → Arrived → Completed.");

        ops.MapPost("/events/{eventId:guid}/checkpoint-scans", async (
            Guid eventId,
            RecordCheckpointScanRequest body,
            IMediator mediator,
            CancellationToken ct) =>
            Results.Ok(await mediator.Send(new RecordCheckpointScanCommand(eventId, body), ct)))
            .WithSummary("Ghi nhận một lượt qua checkpoint")
            .WithDescription(
                "Chủ giải hoặc Coordinator Active. Body không phụ thuộc thiết bị: "
                + "{ scanId, registrationId, checkpointId, scanMethod: RFID|Manual, scannedAtUtc }. "
                + "Cùng scanId trả outcome Duplicate, không phát SSE lần hai. "
                + "Log chỉ tạo khi registration confirmed/finished, có BIB, và checkpoint thuộc đúng cự ly. "
                + "Không unique theo VĐV + checkpoint.");

        ops.MapPost("/events/{eventId:guid}/checkpoint-scans/demo", async (
            Guid eventId,
            GenerateDemoCheckpointScansRequest? body,
            IMediator mediator,
            CancellationToken ct) =>
            Results.Ok(await mediator.Send(
                new GenerateDemoCheckpointScansCommand(eventId, body?.Count),
                ct)))
            .WithSummary("Sinh lượt qua checkpoint demo từ dữ liệu thật của giải")
            .WithDescription(
                "Caller của cùng lệnh ghi scan. Rút ngẫu nhiên VĐV confirmed có BIB và checkpoint đúng cự ly. "
                + "Body: { count?: 1..20 }, mặc định 5. Mỗi dòng là log thật, append-only, có thể hiện trên SSE. "
                + "Không tạo RaceStarted và không ghi passage của module Event.");

        ops.MapGet("/events/{eventId:guid}/race-logs", async (
            Guid eventId,
            Guid? eventDistanceId,
            string? eventType,
            string? bibNumber,
            DateTime? fromUtc,
            DateTime? toUtc,
            long? afterSequence,
            int? page,
            int? pageSize,
            IMediator mediator,
            CancellationToken ct) =>
            Results.Ok(await mediator.Send(
                new ListRaceEventLogsQuery(
                    eventId,
                    eventDistanceId,
                    eventType,
                    bibNumber,
                    fromUtc,
                    toUtc,
                    afterSequence,
                    page ?? 1,
                    pageSize ?? 20),
                ct)))
            .WithSummary("Danh sách timeline của giải")
            .WithDescription(
                "Chủ giải hoặc Coordinator Active. Admin không đi tắt. "
                + "Lọc eventDistanceId, eventType, bibNumber, fromUtc, toUtc, afterSequence. "
                + "Thứ tự OccurredAtUtc rồi sequence. Phân trang page/pageSize.");

        ops.MapGet("/events/{eventId:guid}/race-logs/stream", async (
            Guid eventId,
            HttpContext httpContext,
            IServiceScopeFactory scopeFactory,
            IRaceEventLogBroker broker,
            ILoggerFactory loggerFactory,
            CancellationToken ct) =>
        {
            await RaceEventLogSse.WriteAsync(
                httpContext,
                eventId,
                scopeFactory,
                broker,
                loggerFactory.CreateLogger("RaceEventLogSse"),
                ct);
        })
            .WithSummary("Theo dõi timeline theo SSE")
            .WithDescription(
                "Content-Type: text/event-stream. JWT trên header Authorization. "
                + "Không có Last-Event-ID thì chỉ nhận log mới từ lúc kết nối. "
                + "Có Last-Event-ID thì replay sequence lớn hơn cursor, cộng log ghi trong 30 giây gần nhất. "
                + "Event RaceEventLogCreated. Heartbeat comment mỗi 15 giây. "
                + "Mất quyền thì event RaceEventLogAccessRevoked rồi đóng. "
                + "Client chậm thì RaceEventLogStreamOverrun rồi đóng.");

        ops.MapGet("/events/{eventId:guid}/race-logs/{logId:guid}", async (
            Guid eventId,
            Guid logId,
            IMediator mediator,
            CancellationToken ct) =>
            Results.Ok(await mediator.Send(new GetRaceEventLogQuery(eventId, logId), ct)))
            .WithSummary("Chi tiết một dòng timeline")
            .WithDescription("Log phải thuộc đúng eventId. Chủ giải hoặc Coordinator Active.");

        return app;
    }
}
