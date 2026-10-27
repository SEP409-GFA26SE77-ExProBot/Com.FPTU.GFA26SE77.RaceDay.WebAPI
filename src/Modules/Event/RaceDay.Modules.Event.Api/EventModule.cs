using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using RaceDay.Modules.Event.Application.Events;
using RaceDay.Modules.Event.Application.Disputes;
using RaceDay.Modules.Event.Application.Events.Update;
using RaceDay.Modules.Event.Application.Packages;
using RaceDay.Modules.Event.Application.Registrations;
using RaceDay.Modules.Event.Application.Staff;
using RaceDay.Modules.Event.Application.Supplies;
using RaceDay.Modules.Event.Infrastructure;
using RaceDay.Shared.Application.Abstractions;
using RaceDay.Shared.Web.Auth;
using RaceDay.Shared.Web.OpenApi;
using RaceDay.Modules.Event.Application.Complaints;

namespace RaceDay.Modules.Event.Api;

public static class EventModule
{
    public static IServiceCollection AddEventModule(this IServiceCollection services, IConfiguration configuration)
    {
        // MediatR đăng ký tập trung ở Host (Program.cs)
        services.AddValidatorsFromAssembly(typeof(CreateRaceEventCommand).Assembly);
        services.AddEventInfrastructure(configuration);
        return services;
    }

    public static IEndpointRouteBuilder MapEventEndpoints(this IEndpointRouteBuilder app)
    {
        var publicApi = app.MapGroup("/api/v1/event")
            .WithTags(ApiTags.EventPublic)
            .RequireRateLimiting("public");

        publicApi.MapGet("/", async (IMediator mediator, CancellationToken ct) =>
            Results.Ok(await mediator.Send(new ListPublicEventsQuery(), ct)))
            .WithSummary("Danh sách giải public (đã duyệt / mở ĐK)");

        publicApi.MapGet("/{eventId:guid}", async (Guid eventId, IMediator mediator, CancellationToken ct) =>
            Results.Ok(await mediator.Send(new GetRaceEventByIdQuery(eventId), ct)))
            .WithSummary("Chi tiết giải public");

        publicApi.MapGet("/packages/catalog", async (IMediator mediator, CancellationToken ct) =>
            Results.Ok(await mediator.Send(new GetPackageCatalogQuery(), ct)))
            .WithSummary("Bảng giá gói sự kiện (public) — promo codes from DB");

        publicApi.MapGet("/distances", async ([FromQuery] Guid eventId, IMediator mediator, CancellationToken ct) =>
        {
            var distances = new GetRaceEventDistancesByIdQuery(eventId);
            return Results.Ok(await mediator.Send(distances, ct));
        })
        .WithSummary("Danh sách cự ly tối giản (public) — filter/leaderboard")
        .WithDescription("id, name, distanceKm, type (chip BXH). Không trả giá vé / startAt / endAt. Organizer setup: GET /organizer/{eventId}/distances.");

        // PublicUser = RequireAuthenticatedUser — mọi user đã login (USER/RUNNER/ORGANIZER/…)
        publicApi.MapGet("/{eventId:guid}/distances/{distanceId:guid}/leaderboard", async (
            Guid eventId,
            Guid distanceId,
            string? gender,    // Thêm tham số gender
            int? page,
            int? pageSize,
            IMediator mediator,
            CancellationToken ct) =>
        {
            // Truyền biến gender vào GetDistanceLeaderboardQuery
            var query = new GetDistanceLeaderboardQuery(eventId, distanceId, gender, page ?? 1, pageSize ?? 50);
            return Results.Ok(await mediator.Send(query, ct));
        })
        .WithSummary("Bảng xếp hạng cự ly (Phân trang, Lọc Giới tính)");

        // không bắt role ORGANIZER riêng (không cần duyệt tài khoản organizer).
        var organizerApi = app.MapGroup("/api/v1/event/organizer")
            .WithTags(ApiTags.EventOrganizer)
            .RequireAuthorization(AuthPolicies.PublicUser)
            .RequireRateLimiting("authenticated");

        organizerApi.MapGet("/mine", async (IMediator mediator, CancellationToken ct) =>
            Results.Ok(await mediator.Send(new ListOrganizerEventsQuery(), ct)))
            .WithSummary("Danh sách giải của tôi (organizer)");

        organizerApi.MapPost("/uploads", async (
            IFormFile file,
            string? purpose,
            IFileStorage storage,
            CancellationToken ct) =>
        {
            if (file.Length == 0)
            {
                return Results.BadRequest(new { message = "File trống." });
            }

            // Separate R2/local prefixes: banner ≠ hồ sơ giấy phép ≠ runner health-certs.
            var folder = ResolveOrganizerUploadFolder(purpose);
            if (folder is null)
            {
                return Results.BadRequest(new
                {
                    message = "purpose không hợp lệ. Dùng: banner | document (mặc định document)."
                });
            }

            await using var stream = file.OpenReadStream();
            var stored = await storage.UploadAsync(
                stream,
                file.FileName,
                string.IsNullOrWhiteSpace(file.ContentType) ? "application/octet-stream" : file.ContentType,
                folder: folder,
                ct);
            return Results.Ok(stored);
        })
        .DisableAntiforgery()
        .WithSummary("Organizer upload → fileUrl (banner hoặc hồ sơ)")
        .WithDescription(
            "Query purpose=banner → folder event-banners; "
            + "purpose=document (default) → event-docs. "
            + "Runner giấy khám: POST /api/v1/event/uploads → health-certs.");

        organizerApi.MapPost("/", async (CreateRaceEventCommand command, IMediator mediator, CancellationToken ct) =>
        {
            var result = await mediator.Send(command, ct);
            return Results.Created($"/api/v1/event/{result.Id}", result);
        })
        .WithSummary("Tạo giải (JSON) — wizard cấp phép + cự ly")
        .WithDescription(
            "Auth: user đã đăng nhập (PublicUser). "
            + "Body gọn: name, location, date, startTime?, totalRunners, organizer (+ beneficiaryAccount khi gửi duyệt), "
            + "distances[] (name, distanceKm, capacity, basePrice), "
            + "documents[] sau /organizer/uploads, rules (checklist an toàn), submitForApproval. "
            + "coordinates optional (mặc định 0,0). Không nhận weather/permitDocument/ticketPricing/merchandise/rewards/route.")
        .Accepts<CreateRaceEventCommand>("application/json")
        .Produces<RaceEventDto>(StatusCodes.Status201Created);

        organizerApi.MapPut("/{eventId:guid}/profile", async (Guid eventId, UpdateDraftProfileRequest body, IMediator mediator, CancellationToken ct) =>
        {
            var result = await mediator.Send(new UpdateDraftRaceEventCommand(
                eventId, body.Name, body.Tagline, body.Location, body.Date,
                body.StartTime, body.Description, body.BannerImage,
                body.TotalRunners, body.Organizer, body.Rules,
                body.TicketPricing, body.Rewards,
                body.SetRegistrationWindow,
                body.RegistrationOpensAtUtc, body.RegistrationClosesAtUtc,
                body.Documents, body.EndDate, body.ClearEndDate,
                body.SetBibConfig, body.BibPrefix, body.BibRangeStart, body.BibRangeEnd), ct);
            return Results.Ok(result);
        })
        .WithSummary("Cập nhật hồ sơ giải draft/rejected (partial update)")
        .WithDescription(
            "Organizer điền dần: name, tagline, location, date, startTime, description, bannerImage, "
            + "totalRunners, organizer (bank), rules, ticketPricing, rewards, documents. "
            + "setRegistrationWindow + registrationOpensAtUtc/ClosesAtUtc (closes ≤ packageExpires). "
            + "setBibConfig đã ngưng — dùng PUT /organizer/{eventId}/bib-configs (dải theo từng cự ly).");

        organizerApi.MapGet("/{eventId:guid}/distances", async (
            Guid eventId, IMediator mediator, CancellationToken ct) =>
            Results.Ok(await mediator.Send(new GetOrganizerDistancesQuery(eventId), ct)))
            .WithSummary("Chi tiết cự ly + ticketPricing (organizer)")
            .WithDescription(
                "Chủ giải / admin. Trả distances (name, km, capacity, basePrice, startAt, endAt) + ticketPricing. "
                + "Không lẫn GET public /event/distances (filter tối giản).")
            .Produces<OrganizerDistancesDetailDto>();

        organizerApi.MapPut("/{eventId:guid}/bib-configs", async (
            Guid eventId, ReplaceDistanceBibConfigsRequest body, IMediator mediator, CancellationToken ct) =>
        {
            var result = await mediator.Send(new ReplaceEventDistanceBibConfigsCommand(
                eventId, body.Distances), ct);
            return Results.Ok(result);
        })
        .WithSummary("Cấu hình dải BIB theo từng cự ly")
        .WithDescription(
            "Mỗi cự ly: prefix (gợi ý số km: 15/21/42…) + từ số/đến số 1–9999 (cấp dạng 0001). "
            + "Không trùng dải số giữa các cự ly; tổng dải ≤ quy mô gói; mỗi dải ≥ sức chứa cự ly.")
        .Accepts<ReplaceDistanceBibConfigsRequest>("application/json")
        .Produces<RaceEventDto>();

        organizerApi.MapPut("/{eventId:guid}/distances", async (
            Guid eventId, ReplaceDistancesRequest body, IMediator mediator, CancellationToken ct) =>
        {
            var result = await mediator.Send(new ReplaceEventDistancesCommand(
                eventId, body.Distances, body.TicketPricing), ct);
            return Results.Ok(result);
        })
        .WithSummary("Cập nhật / thay thế danh sách cự ly (+ ticketPricing)")
        .WithDescription(
            "Gửi id để giữ cự ly (map/staff/reg không gãy); "
            + "không id = thêm mới; bỏ id khỏi list = xóa (chặn nếu đã có ĐK). "
            + "Body: name, distanceKm, capacity, basePrice, type? (chip BXH: 3KM/HM/M/OTHER), startAt?, endAt?. Currency luôn VND. "
            + "Tổng capacity ≤ TotalRunnersCap khi có gói. Map route vẫn theo distanceKm.");

        organizerApi.MapPost("/packages/purchases", async (CreatePackagePurchaseRequest body, IMediator mediator, CancellationToken ct) =>
        {
            var result = await mediator.Send(new CreatePackagePurchaseCommand(
                body.OrgName, body.OrgIntro, body.OrgAddress, body.InvoiceInfo,
                body.DurationMonths, body.ParticipantCap, body.PromoCode), ct);
            return Results.Created($"/api/v1/event/organizer/packages/purchases/{result.Id}", result);
        })
        .WithSummary("Tạo đơn mua gói sự kiện → pending_payment")
        .WithDescription(
            "Organizer chọn gói (tháng × sức chứa) + thông tin BTC + promo. "
            + "Tiếp: POST /api/v1/payment/checkout/package.")
        .Produces<PackagePurchaseDto>(StatusCodes.Status201Created);

        organizerApi.MapGet("/packages/purchases/{purchaseId:guid}", async (Guid purchaseId, IMediator mediator, CancellationToken ct) =>
            Results.Ok(await mediator.Send(new GetPackagePurchaseQuery(purchaseId), ct)))
            .WithSummary("Chi tiết đơn mua gói sự kiện");

        organizerApi.MapPost("/packages/purchases/{purchaseId:guid}/cancel", async (Guid purchaseId, IMediator mediator, CancellationToken ct) =>
            Results.Ok(await mediator.Send(new CancelPackagePurchaseCommand(purchaseId), ct)))
            .WithSummary("Hủy đơn mua gói đang chờ thanh toán")
            .WithDescription("Chỉ pending_payment. FE: nút «Hủy thanh toán» trên checkout gói.");

        organizerApi.MapPost("/{eventId:guid}/submit", async (Guid eventId, IMediator mediator, CancellationToken ct) =>
            Results.Ok(await mediator.Send(new SubmitRaceEventCommand(eventId), ct)))
            .WithSummary("Gửi duyệt từ Draft / Rejected");

        organizerApi.MapPost("/{eventId:guid}/open-registration", async (Guid eventId, IMediator mediator, CancellationToken ct) =>
            Results.Ok(await mediator.Send(new OpenRegistrationCommand(eventId), ct)))
            .WithSummary("Mở đăng ký VĐV — bước riêng sau khi admin đã duyệt (Approved)")
            .WithDescription(
                "Duyệt giải không tự mở ĐK. Sau approve, status = approved (public có thể xem). "
                + "Gọi API này để chuyển → registration-open thì VĐV mới POST .../registrations được.");

        organizerApi.MapPost("/{eventId:guid}/close-registration", async (Guid eventId, IMediator mediator, CancellationToken ct) =>
            Results.Ok(await mediator.Send(new CloseRegistrationCommand(eventId), ct)))
            .WithSummary("Đóng đăng ký VĐV (registration-open → closed)");

        organizerApi.MapGet("/{eventId:guid}/registrations", async (Guid eventId, IMediator mediator, CancellationToken ct) =>
            Results.Ok(await mediator.Send(new ListEventRegistrationsQuery(eventId), ct)))
            .WithSummary("Danh sách đăng ký của giải");

        organizerApi.MapPost("/registrations/{registrationId:guid}/approve", async (
            Guid registrationId, ApproveRegistrationRequest? body, IMediator mediator, CancellationToken ct) =>
            Results.Ok(await mediator.Send(new ApproveRegistrationCommand(registrationId, body?.BibNumber), ct)))
            .WithSummary("Duyệt đăng ký VĐV (bibNumber null → tự cấp số)");

        organizerApi.MapPost("/registrations/{registrationId:guid}/reject", async (
            Guid registrationId, RejectRequest body, IMediator mediator, CancellationToken ct) =>
            Results.Ok(await mediator.Send(new RejectRegistrationCommand(registrationId, body.Reason), ct)))
            .WithSummary("Từ chối đăng ký VĐV");

        organizerApi.MapGet("/{eventId:guid}/staff", async (
            Guid eventId, string? role, IMediator mediator, CancellationToken ct) =>
            Results.Ok(await mediator.Send(new ListEventStaffQuery(eventId, role), ct)))
            .WithSummary("Danh sách nhân sự giải (coordinator|photographer|volunteer)");

        organizerApi.MapPost("/{eventId:guid}/staff/invite", async (
            Guid eventId, InviteEventStaffRequest body, IMediator mediator, CancellationToken ct) =>
            Results.Ok(await mediator.Send(new InviteEventStaffCommand(eventId, body), ct)))
            .WithSummary("Mời nhân sự qua email");

        organizerApi.MapPost("/{eventId:guid}/staff", async (
            Guid eventId, AddEventStaffRequest body, IMediator mediator, CancellationToken ct) =>
            Results.Ok(await mediator.Send(new AddEventStaffCommand(eventId, body), ct)))
            .WithSummary("Thêm nhân sự trực tiếp (active)");

        organizerApi.MapPatch("/{eventId:guid}/staff/{memberId:guid}/station", async (
            Guid eventId, Guid memberId, AssignEventStaffStationRequest body, IMediator mediator, CancellationToken ct) =>
            Results.Ok(await mediator.Send(new AssignEventStaffStationCommand(eventId, memberId, body), ct)))
            .WithSummary("Gán checkpoint / điểm tiếp tế / nhiệm vụ");

        organizerApi.MapDelete("/{eventId:guid}/staff/{memberId:guid}", async (
            Guid eventId, Guid memberId, IMediator mediator, CancellationToken ct) =>
        {
            await mediator.Send(new RevokeEventStaffCommand(eventId, memberId), ct);
            return Results.NoContent();
        })
            .WithSummary("Gỡ nhân sự khỏi giải");

        organizerApi.MapGet("/{eventId:guid}/supplies", async (
            Guid eventId, IMediator mediator, CancellationToken ct) =>
            Results.Ok(await mediator.Send(new ListEventSuppliesQuery(eventId), ct)))
            .WithSummary("Danh sách vật phẩm tiếp tế (đơn vị thùng)");

        organizerApi.MapPut("/{eventId:guid}/supplies", async (
            Guid eventId, UpdateEventSuppliesRequest body, IMediator mediator, CancellationToken ct) =>
            Results.Ok(await mediator.Send(new ReplaceEventSuppliesCommand(eventId, body), ct)))
            .WithSummary("Thay thế toàn bộ danh sách vật phẩm tiếp tế");

        organizerApi.MapPost("/{eventId:guid}/supplies", async (
            Guid eventId, CreateEventSupplyRequest body, IMediator mediator, CancellationToken ct) =>
            Results.Ok(await mediator.Send(new AddEventSupplyCommand(eventId, body), ct)))
            .WithSummary("Thêm 1 vật phẩm tiếp tế");

        organizerApi.MapDelete("/{eventId:guid}/supplies/{supplyId:guid}", async (
            Guid eventId, Guid supplyId, IMediator mediator, CancellationToken ct) =>
        {
            await mediator.Send(new DeleteEventSupplyCommand(eventId, supplyId), ct);
            return Results.NoContent();
        })
            .WithSummary("Xóa 1 vật phẩm tiếp tế");

        // ── Endpoints Khiếu Nại cho Organizer ──
        organizerApi.MapGet("/complaints", async (
            [FromQuery] Guid? eventId,
            [FromQuery] string? status,
            [FromQuery] DateTime? fromDate,
            [FromQuery] DateTime? toDate,
            [FromQuery] int? page,
            [FromQuery] int? pageSize,
            IMediator mediator,
            CancellationToken ct) =>
            Results.Ok(await mediator.Send(new ListOrganizerComplaintsQuery(eventId, status, fromDate, toDate, page ?? 1, pageSize ?? 10), ct)))
            .WithSummary("Organizer xem danh sách khiếu nại gom nhóm theo giải")
            .WithDescription("Hỗ trợ lọc theo trạng thái (Pending/Accepted/Rejected), khoảng thời gian và phân trang.");

        organizerApi.MapPost("/complaints/{complaintId:guid}/reply", async (
            Guid complaintId,
            ReplyComplaintRequest body,
            IMediator mediator,
            CancellationToken ct) =>
            Results.Ok(await mediator.Send(new ReplyComplaintCommand(complaintId, body.ResponseContent, body.Decision), ct)))
            .WithSummary("Organizer gửi phản hồi và quyết định khiếu nại (Accepted/Rejected)");

        var userApi = app.MapGroup("/api/v1/event")
            .WithTags(ApiTags.EventRunner)
            .RequireAuthorization(AuthPolicies.PublicUser)
            .RequireRateLimiting("public");

        userApi.MapPost("/uploads", async (
            IFormFile file,
            IFileStorage storage,
            CancellationToken ct) =>
        {
            if (file.Length == 0)
            {
                return Results.BadRequest(new { message = "File trống." });
            }

            await using var stream = file.OpenReadStream();
            var stored = await storage.UploadAsync(
                stream,
                file.FileName,
                string.IsNullOrWhiteSpace(file.ContentType) ? "application/octet-stream" : file.ContentType,
                folder: "health-certs",
                ct);
            return Results.Ok(stored);
        })
        .DisableAntiforgery()
        .WithSummary("VĐV upload giấy khám → fileUrl");

        userApi.MapGet("/registrations/mine", async (IMediator mediator, CancellationToken ct) =>
            Results.Ok(await mediator.Send(new ListMyRegistrationsQuery(), ct)))
            .WithSummary("Đăng ký của tôi (FE /my-registrations)");

        userApi.MapGet("/joined/recent", async (int? page, int? pageSize, IMediator mediator, CancellationToken ct) =>
            Results.Ok(await mediator.Send(new ListRecentJoinedEventsQuery(page ?? 1, pageSize ?? 20), ct)))
            .WithSummary("Các giải runner đã tham gia trong 1 tháng gần đây")
            .WithDescription("Theo ngày diễn ra giải, từ ngày UTC hiện tại lùi 1 tháng đến hôm nay (bao gồm hai đầu). Chỉ lấy đăng ký confirmed/finished; mỗi giải xuất hiện một lần. Có phân trang.")
            .Produces<RunnerPageDto<RecentJoinedEventDto>>();

        userApi.MapGet("/disputes/mine", async (int? page, int? pageSize, IMediator mediator, CancellationToken ct) =>
            Results.Ok(await mediator.Send(new ListMyEventDisputesQuery(page ?? 1, pageSize ?? 10), ct)))
            .WithSummary("Danh sách khiếu nại của runner đang đăng nhập (có phân trang)")
            .Produces<RunnerPageDto<EventDisputeDto>>();

        userApi.MapPost("/disputes", async (CreateEventDisputeCommand body, IMediator mediator, CancellationToken ct) =>
        {
            var result = await mediator.Send(body, ct);
            return Results.Created("/api/v1/event/disputes/mine", result);
        })
            .WithSummary("Runner gửi khiếu nại cho giải đã tham gia trong 1 tháng gần đây")
            .Produces<EventDisputeDto>(StatusCodes.Status201Created);
        userApi.MapGet("/leaderboards/mine", async (IMediator mediator, CancellationToken ct) =>
            Results.Ok(await mediator.Send(new GetMyLeaderboardResultsQuery(), ct)))
            .WithSummary("Bảng xếp hạng / Thành tích cá nhân của Runner đang đăng nhập")
            .WithDescription("Trả về danh sách kết quả các giải & cự ly mà Runner đã hoàn thành (Finished) kèm thứ hạng (Rank), Total Time, Pace.");

        userApi.MapGet("/registrations/{registrationId:guid}", async (Guid registrationId, IMediator mediator, CancellationToken ct) =>
            Results.Ok(await mediator.Send(new GetMyRegistrationByIdQuery(registrationId), ct)))
            .WithSummary("Chi tiết đăng ký / ticket (BIB + qrPayload khi confirmed)");

        userApi.MapPost("/registrations/{registrationId:guid}/cancel", async (Guid registrationId, IMediator mediator, CancellationToken ct) =>
            Results.Ok(await mediator.Send(new CancelUnpaidRegistrationCommand(registrationId), ct)))
            .WithSummary("VĐV hủy đơn chờ thanh toán");

        userApi.MapPut("/registrations/{registrationId:guid}/resubmit", async (
            Guid registrationId, ResubmitRegistrationRequest body, IMediator mediator, CancellationToken ct) =>
            Results.Ok(await mediator.Send(body.ToCommand(registrationId), ct)))
            .WithSummary("VĐV đã paid bị từ chối → sửa hồ sơ & nộp lại pending_approval (không TT lại)");

        userApi.MapGet("/staff/me", async (IMediator mediator, CancellationToken ct) =>
            Results.Ok(await mediator.Send(new ListMyStaffAssignmentsQuery(), ct)))
            .WithSummary("Các giải staff/volunteer đang được assign (Active)");

        // Endpoint /start
        userApi.MapPost("/start", async (StartRequestDto body, IMediator mediator, CancellationToken ct) =>
            Results.Ok(await mediator.Send(
                new StartRegistrationCommand(body.EventId, body.Bib), ct)))
            .WithSummary("Staff/organizer ghi nhận xuất phát (Start) cho VĐV")
            .WithDescription("Body: { eventId, bib }.")
            .Produces<StartResultDto>(StatusCodes.Status200OK);

        // Endpoint /finish
        userApi.MapPost("/finish", async (FinishRequestDto body, IMediator mediator, CancellationToken ct) =>
            Results.Ok(await mediator.Send(
                new FinishRegistrationCommand(body.EventId, body.Bib, body.DistanceId), ct)))
            .WithSummary("Staff/organizer ghi nhận finish cho VĐV")
            .WithDescription("Body: { eventId, bib, distanceId }.")
            .Produces<FinishResultDto>(StatusCodes.Status200OK);

        userApi.MapPost("/check-in", async (CheckInRequestDto body, IMediator mediator, CancellationToken ct) =>
            Results.Ok(await mediator.Send(
                new CheckInRegistrationCommand(body.EventId, body.Bib, body.QrPayload), ct)))
            .WithSummary("Staff/organizer check-in VĐV bằng BIB hoặc QR payload")
            .WithDescription(
                "Body: { eventId, bib? , qrPayload? }. QR JSON từ vé: { user_id, bib, event_id, distance }. "
                + "Quyền: Admin, chủ giải, hoặc staff Active đã LinkUser.");

        // Path riêng (không dùng GET /check-in) — tránh 405 khi container chưa rebuild
        // hoặc proxy chỉ cho phép POST trên /check-in.
        userApi.MapGet("/check-ins", async (Guid eventId, IMediator mediator, CancellationToken ct) =>
            Results.Ok(await mediator.Send(new ListEventCheckInsQuery(eventId), ct)))
            .WithSummary("Danh sách VĐV confirmed + trạng thái / thời điểm check-in")
            .WithDescription("Query: eventId. Quyền giống POST /check-in.");

        userApi.MapPost("/{eventId:guid}/registrations", async (
            Guid eventId, CreateRegistrationRequest body, IMediator mediator, CancellationToken ct) =>
        {
            var result = await mediator.Send(body.ToCommand(eventId), ct);
            return Results.Created($"/api/v1/event/registrations/{result.Id}", result);
        })
        .WithSummary("VĐV tạo đăng ký → pending_payment")
        .WithDescription(
            "Giải phải registration-open. Tiếp: POST /api/v1/payment/checkout. "
            + "PayOS PAID → pending_approval. Organizer approve → confirmed + bib + qrPayload.");

        // ── Endpoint Khiếu Nại cho VĐV ──
        userApi.MapPost("/complaints", async (
            CreateComplaintRequest body,
            IMediator mediator,
            CancellationToken ct) =>
        {
            var result = await mediator.Send(new CreateComplaintCommand(body.EventId, body.DistanceId, body.Title, body.Content, body.EvidenceLinks), ct);
            return Results.Created($"/api/v1/event/complaints/{result.Id}", result);
        })
        .WithSummary("VĐV gửi thông tin khiếu nại tới BTC")
        .WithDescription("Body: { eventId, distanceId, title, content, evidenceLinks (mảng URL) }.")
        .Produces<EventComplaintDto>(StatusCodes.Status201Created);

        userApi.MapGet("/complaints/mine", async (
            [FromQuery] Guid? eventId,
            [FromQuery] string? status,
            [FromQuery] int? page,
            [FromQuery] int? pageSize,
            IMediator mediator,
            CancellationToken ct) =>
            Results.Ok(await mediator.Send(new GetMyComplaintsQuery(eventId, status, page ?? 1, pageSize ?? 10), ct)))
        .WithSummary("VĐV xem danh sách khiếu nại của chính mình")
        .WithDescription("Trả về danh sách khiếu nại có phân trang và lọc theo trạng thái (Pending/Accepted/Rejected).");

        var adminApi = app.MapGroup("/api/v1/event/admin")
            .WithTags(ApiTags.EventAdmin)
            .RequireAuthorization(AuthPolicies.AdminOnly)
            .RequireRateLimiting("admin");

        adminApi.MapGet("/pending", async (IMediator mediator, CancellationToken ct) =>
            Results.Ok(await mediator.Send(new ListPendingEventsQuery(), ct)))
            .WithSummary("Danh sách giải chờ duyệt");

        adminApi.MapGet("/{eventId:guid}", async (Guid eventId, IMediator mediator, CancellationToken ct) =>
            Results.Ok(await mediator.Send(new GetRaceEventByIdQuery(eventId), ct)))
            .WithSummary("Chi tiết giải (kể cả chưa public)");

        adminApi.MapPost("/{eventId:guid}/approve", async (
            Guid eventId, ApproveEventRequest? body, IMediator mediator, CancellationToken ct) =>
            Results.Ok(await mediator.Send(new ApproveRaceEventCommand(eventId, body?.OpenRegistration ?? false), ct)))
            .WithSummary("Duyệt giải (mặc định KHÔNG mở ĐK)")
            .WithDescription(
                "Chỉ chuyển PendingApproval → Approved. Muốn mở ĐK ngay: body { \"openRegistration\": true }. "
                + "Không thì organizer/admin gọi POST .../open-registration sau.");

        adminApi.MapPost("/{eventId:guid}/reject", async (
            Guid eventId, RejectRequest body, IMediator mediator, CancellationToken ct) =>
            Results.Ok(await mediator.Send(new RejectRaceEventCommand(eventId, body.Reason), ct)))
            .WithSummary("Từ chối giải (cần reason)");

        // ── Admin promo-code management ───────────────────────────────

        var promoApi = app.MapGroup("/api/v1/event/admin/promos")
            .WithTags(ApiTags.EventAdmin)
            .RequireAuthorization(AuthPolicies.AdminOnly)
            .RequireRateLimiting("admin");

        promoApi.MapGet("/", async (IMediator mediator, CancellationToken ct) =>
            Results.Ok(await mediator.Send(new ListPromoCodesQuery(), ct)))
            .WithSummary("Danh sách tất cả promo codes (Admin)");

        promoApi.MapPost("/", async (CreatePromoCodeRequest body, IMediator mediator, CancellationToken ct) =>
        {
            var result = await mediator.Send(
                new CreatePromoCodeCommand(body.Code, body.PercentOff, body.ExpiresAtUtc, body.MaxRedemptions), ct);
            return Results.Created($"/api/v1/event/admin/promos/{result.Id}", result);
        })
        .WithSummary("Tạo promo code mới (Admin)")
        .Produces<PromoCodeDto>(StatusCodes.Status201Created);

        promoApi.MapPatch("/{promoId:guid}", async (Guid promoId, UpdatePromoCodeRequest body, IMediator mediator, CancellationToken ct) =>
        {
            var result = await mediator.Send(new UpdatePromoCodeCommand(
                promoId,
                body.Active,
                body.ExpiresAtUtc,
                body.ClearExpiry ?? false,
                body.MaxRedemptions,
                body.ClearMaxRedemptions ?? false), ct);
            return Results.Ok(result);
        })
        .WithSummary("Cập nhật promo code (activate/deactivate/expiry/max) (Admin)");

        // ── Admin package duration / capacity catalog ─────────────────

        var durationApi = app.MapGroup("/api/v1/event/admin/package-durations")
            .WithTags(ApiTags.EventAdmin)
            .RequireAuthorization(AuthPolicies.AdminOnly)
            .RequireRateLimiting("admin");

        durationApi.MapGet("/", async (IMediator mediator, CancellationToken ct) =>
            Results.Ok(await mediator.Send(new ListPackageDurationsQuery(), ct)))
            .WithSummary("Danh sách thời hạn gói (Admin)");

        durationApi.MapPost("/", async (CreatePackageDurationRequest body, IMediator mediator, CancellationToken ct) =>
        {
            var result = await mediator.Send(
                new CreatePackageDurationCommand(body.Name, body.Months, body.Price, body.SortOrder), ct);
            return Results.Created($"/api/v1/event/admin/package-durations/{result.Id}", result);
        })
        .WithSummary("Tạo thời hạn gói (name, months, price)")
        .Produces<PackageDurationOptionDto>(StatusCodes.Status201Created);

        durationApi.MapPatch("/{id:guid}", async (Guid id, UpdatePackageDurationRequest body, IMediator mediator, CancellationToken ct) =>
            Results.Ok(await mediator.Send(new UpdatePackageDurationCommand(
                id, body.Name, body.Price, body.SortOrder, body.Active), ct)))
            .WithSummary("Cập nhật thời hạn gói (Admin)");

        var capacityApi = app.MapGroup("/api/v1/event/admin/package-capacities")
            .WithTags(ApiTags.EventAdmin)
            .RequireAuthorization(AuthPolicies.AdminOnly)
            .RequireRateLimiting("admin");

        capacityApi.MapGet("/", async (IMediator mediator, CancellationToken ct) =>
            Results.Ok(await mediator.Send(new ListPackageCapacitiesQuery(), ct)))
            .WithSummary("Danh sách quy mô VĐV gói (Admin)");

        capacityApi.MapPost("/", async (CreatePackageCapacityRequest body, IMediator mediator, CancellationToken ct) =>
        {
            var result = await mediator.Send(
                new CreatePackageCapacityCommand(body.Name, body.MaxParticipants, body.AddOnFee, body.SortOrder), ct);
            return Results.Created($"/api/v1/event/admin/package-capacities/{result.Id}", result);
        })
        .WithSummary("Tạo quy mô VĐV gói (name, maxParticipants, addOnFee)")
        .Produces<PackageCapacityOptionDto>(StatusCodes.Status201Created);

        capacityApi.MapPatch("/{id:guid}", async (Guid id, UpdatePackageCapacityRequest body, IMediator mediator, CancellationToken ct) =>
            Results.Ok(await mediator.Send(new UpdatePackageCapacityCommand(
                id, body.Name, body.AddOnFee, body.SortOrder, body.Active), ct)))
            .WithSummary("Cập nhật quy mô VĐV gói (Admin)");

        return app;
    }

    /// <summary>Maps upload purpose → storage folder prefix (same bucket, different keys).</summary>
    private static string? ResolveOrganizerUploadFolder(string? purpose) =>
        (purpose ?? "document").Trim().ToLowerInvariant() switch
        {
            "" or "document" or "doc" or "docs" => "event-docs",
            "banner" or "image" or "cover" => "event-banners",
            _ => null
        };

    private sealed record RejectRequest(string Reason);
    private sealed record ApproveEventRequest(bool OpenRegistration = false);
    private sealed record ApproveRegistrationRequest(string? BibNumber);
    private sealed record CreatePromoCodeRequest(string Code, decimal PercentOff, DateTime? ExpiresAtUtc, int? MaxRedemptions);
    private sealed record UpdatePromoCodeRequest(bool? Active, DateTime? ExpiresAtUtc, bool? ClearExpiry, int? MaxRedemptions, bool? ClearMaxRedemptions);
    private sealed record CreatePackageDurationRequest(string Name, int Months, decimal Price, int? SortOrder);
    private sealed record UpdatePackageDurationRequest(string? Name, decimal? Price, int? SortOrder, bool? Active);
    private sealed record CreatePackageCapacityRequest(string Name, int MaxParticipants, decimal AddOnFee, int? SortOrder);
    private sealed record UpdatePackageCapacityRequest(string? Name, decimal? AddOnFee, int? SortOrder, bool? Active);

    private sealed record UpdateDraftProfileRequest(
        string? Name,
        string? Tagline,
        string? Location,
        DateOnly? Date,
        TimeOnly? StartTime,
        string? Description,
        string? BannerImage,
        int? TotalRunners,
        OrganizerDto? Organizer,
        object? Rules,
        object? TicketPricing = null,
        object? Rewards = null,
        bool SetRegistrationWindow = false,
        DateTime? RegistrationOpensAtUtc = null,
        DateTime? RegistrationClosesAtUtc = null,
        IReadOnlyList<EventDocumentDto>? Documents = null,
        DateOnly? EndDate = null,
        bool ClearEndDate = false,
        bool SetBibConfig = false,
        string? BibPrefix = null,
        int? BibRangeStart = null,
        int? BibRangeEnd = null);

    private sealed record ReplaceDistancesRequest(
        IReadOnlyList<ReplaceDistanceInput> Distances,
        object? TicketPricing = null);

    private sealed record ReplaceDistanceBibConfigsRequest(
        IReadOnlyList<DistanceBibConfigInput> Distances);

    private sealed record CreateRegistrationRequest(
        Guid DistanceId,
        string FullName,
        string Email,
        string Phone,
        DateOnly? DateOfBirth,
        string? Gender,
        string? IdNumber,
        int? HeightCm,
        int? WeightKg,
        string? BloodType,
        string? EmergencyName,
        string? EmergencyPhone,
        string? ShirtType,
        string? ShirtSize,
        string? ShirtColor,
        IReadOnlyList<string>? MedicalCodes,
        string? HealthCertFileName,
        string? HealthCertUrl,
        bool LiabilityAccepted)
    {
        public CreateRegistrationCommand ToCommand(Guid eventId) => new(
            eventId, DistanceId, FullName, Email, Phone, DateOfBirth, Gender, IdNumber,
            HeightCm, WeightKg, BloodType, EmergencyName, EmergencyPhone,
            ShirtType, ShirtSize, ShirtColor, MedicalCodes, HealthCertFileName, HealthCertUrl, LiabilityAccepted);
    }

    private sealed record ResubmitRegistrationRequest(
        string FullName,
        string Email,
        string Phone,
        DateOnly? DateOfBirth,
        string? Gender,
        string? IdNumber,
        int? HeightCm,
        int? WeightKg,
        string? BloodType,
        string? EmergencyName,
        string? EmergencyPhone,
        string? ShirtType,
        string? ShirtSize,
        string? ShirtColor,
        IReadOnlyList<string>? MedicalCodes,
        string? HealthCertFileName,
        string? HealthCertUrl,
        bool LiabilityAccepted)
    {
        public ResubmitRejectedRegistrationCommand ToCommand(Guid registrationId) => new(
            registrationId, FullName, Email, Phone, DateOfBirth, Gender, IdNumber,
            HeightCm, WeightKg, BloodType, EmergencyName, EmergencyPhone,
            ShirtType, ShirtSize, ShirtColor, MedicalCodes, HealthCertFileName, HealthCertUrl, LiabilityAccepted);
    }

    // ĐÃ CHUYỂN VÀO TRONG CLASS EventModule
    private sealed record CreateComplaintRequest(Guid EventId, Guid DistanceId, string Title, string Content, IReadOnlyList<string>? EvidenceLinks);
    private sealed record ReplyComplaintRequest(string ResponseContent, string Decision);
}