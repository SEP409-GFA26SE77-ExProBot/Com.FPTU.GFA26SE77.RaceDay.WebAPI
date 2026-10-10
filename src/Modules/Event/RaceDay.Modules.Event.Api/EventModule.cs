using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using RaceDay.Modules.Event.Application.Events;
using RaceDay.Modules.Event.Application.Registrations;
using RaceDay.Modules.Event.Infrastructure;
using RaceDay.Shared.Application.Abstractions;
using RaceDay.Shared.Web.Auth;
using RaceDay.Shared.Web.OpenApi;

namespace RaceDay.Modules.Event.Api;

public static class EventModule
{
    public static IServiceCollection AddEventModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(typeof(CreateRaceEventCommand).Assembly));
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

        // PublicUser = RequireAuthenticatedUser — mọi user đã login (USER/RUNNER/ORGANIZER/…)
        // không bắt role ORGANIZER riêng (không cần duyệt tài khoản organizer).
        var organizerApi = app.MapGroup("/api/v1/event/organizer")
            .WithTags(ApiTags.EventOrganizer)
            .RequireAuthorization(AuthPolicies.PublicUser)
            .RequireRateLimiting("public");

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
            + "distances[] (type/name/km/capacity/price; start/finish/elevation optional), "
            + "documents[] sau /organizer/uploads, rules (checklist an toàn), submitForApproval. "
            + "coordinates optional (mặc định 0,0). Không nhận weather/permitDocument/ticketPricing/merchandise/rewards/route.")
        .Accepts<CreateRaceEventCommand>("application/json")
        .Produces<RaceEventDto>(StatusCodes.Status201Created);

        organizerApi.MapPost("/{eventId:guid}/submit", async (Guid eventId, IMediator mediator, CancellationToken ct) =>
            Results.Ok(await mediator.Send(new SubmitRaceEventCommand(eventId), ct)))
            .WithSummary("Gửi duyệt từ Draft / Rejected");

        organizerApi.MapPost("/{eventId:guid}/open-registration", async (Guid eventId, IMediator mediator, CancellationToken ct) =>
            Results.Ok(await mediator.Send(new OpenRegistrationCommand(eventId), ct)))
            .WithSummary("Mở đăng ký VĐV — bước riêng sau khi admin đã duyệt (Approved)")
            .WithDescription(
                "Duyệt giải không tự mở ĐK. Sau approve, status = approved (public có thể xem). "
                + "Gọi API này để chuyển → registration-open thì VĐV mới POST .../registrations được.");

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

        userApi.MapGet("/registrations/{registrationId:guid}", async (Guid registrationId, IMediator mediator, CancellationToken ct) =>
            Results.Ok(await mediator.Send(new GetMyRegistrationByIdQuery(registrationId), ct)))
            .WithSummary("Chi tiết đăng ký / ticket (BIB + qrPayload khi confirmed)");

        userApi.MapPost("/registrations/{registrationId:guid}/cancel", async (Guid registrationId, IMediator mediator, CancellationToken ct) =>
            Results.Ok(await mediator.Send(new CancelUnpaidRegistrationCommand(registrationId), ct)))
            .WithSummary("VĐV hủy đơn chờ thanh toán");

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
}
