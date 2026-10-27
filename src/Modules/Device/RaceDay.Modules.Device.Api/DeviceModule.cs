using System.Security.Claims;
using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using RaceDay.Modules.Device.Application.Auth;
using RaceDay.Modules.Device.Application.Sos;
using RaceDay.Modules.Device.Infrastructure;
using RaceDay.Shared.Domain.Exceptions;
using RaceDay.Shared.Web.Auth;
using RaceDay.Shared.Web.OpenApi;

namespace RaceDay.Modules.Device.Api;

public static class DeviceModule
{
    public static IServiceCollection AddDeviceModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddValidatorsFromAssembly(typeof(RequestDeviceCodeCommandValidator).Assembly);
        services.AddDeviceInfrastructure(configuration);
        return services;
    }

    public static IEndpointRouteBuilder MapDeviceEndpoints(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api/v1/device")
            .WithTags(ApiTags.Device)
            .RequireRateLimiting("public");

        api.MapGet("/status", () => Results.Ok(new { module = "device", status = "ready", mode = "wear-sos" }))
            .WithSummary("Trạng thái module Device (Wear Device Flow + SOS)");

        // —— Watch anonymous (RFC 8628) ——
        var auth = api.MapGroup("/auth");

        auth.MapPost("/code", async (DeviceCodeRequestDto body, IMediator mediator, CancellationToken ct) =>
            Results.Ok(await mediator.Send(new RequestDeviceCodeCommand(body.DeviceId), ct)))
            .AllowAnonymous()
            .RequireRateLimiting("device-code")
            .WithSummary("Watch xin device_code + user_code (hiển thị QR)")
            .WithDescription("Body: { deviceId: uuid }. Trả deviceCode (bí mật), userCode, verificationUrlComplete.");

        auth.MapPost("/token", async (DeviceTokenRequestDto body, IMediator mediator, CancellationToken ct) =>
            Results.Ok(await mediator.Send(new PollDeviceTokenCommand(body.DeviceCode), ct)))
            .AllowAnonymous()
            .RequireRateLimiting("device-token")
            .WithSummary("Watch poll lấy accessToken (scope sos:create)")
            .WithDescription(
                "Chưa duyệt → 400 error=authorization_pending. Hết hạn → expired_token. "
                + "OK → accessToken + refreshToken.");

        // —— Runner phone (IAM JWT — không dùng wear token) ——
        var link = api.MapGroup("/link").RequireAuthorization(AuthPolicies.HumanUser);

        link.MapGet("/{userCode}", async (string userCode, IMediator mediator, CancellationToken ct) =>
            Results.Ok(await mediator.Send(new GetLinkPreviewQuery(userCode), ct)))
            .WithSummary("Xem mã liên kết trước khi xác nhận");

        link.MapPost("/{userCode}/approve", async (
            string userCode,
            ApproveLinkBody? body,
            IMediator mediator,
            CancellationToken ct) =>
        {
            await mediator.Send(new ApproveLinkCommand(userCode, body?.DeviceName), ct);
            return Results.NoContent();
        })
            .Accepts<ApproveLinkBody?>("application/json")
            .WithSummary("Runner xác nhận liên kết đồng hồ")
            .WithDescription("Body optional: { deviceName? }. Có thể gửi {}.");

        var linked = api.MapGroup("/linked-devices").RequireAuthorization(AuthPolicies.HumanUser);

        linked.MapGet("/", async (IMediator mediator, CancellationToken ct) =>
            Results.Ok(await mediator.Send(new ListMyLinkedDevicesQuery(), ct)))
            .WithSummary("Danh sách đồng hồ đã liên kết");

        linked.MapDelete("/{id:guid}", async (Guid id, IMediator mediator, CancellationToken ct) =>
        {
            await mediator.Send(new RevokeLinkedDeviceCommand(id), ct);
            return Results.NoContent();
        })
            .WithSummary("Thu hồi đồng hồ");

        // —— SOS (JWT wear scope) ——
        var sos = api.MapGroup("/sos").RequireAuthorization(AuthPolicies.SosWrite);

        sos.MapPost("/", async (CreateSosRequest body, HttpRequest http, IMediator mediator, CancellationToken ct) =>
        {
            var (runnerId, deviceId) = RequireWearIdentity(http.HttpContext.User);
            return Results.Ok(await mediator.Send(new CreateSosCommand(body, deviceId, runnerId), ct));
        })
            .WithSummary("Tạo SOS (idempotent theo id)")
            .WithDescription("Body: { id, lat, lng, accuracy?, recordedAtUtc, battery? }.");

        sos.MapPost("/{id:guid}/locations", async (
            Guid id, AppendSosLocationsRequest body, HttpRequest http, IMediator mediator, CancellationToken ct) =>
        {
            var (runnerId, _) = RequireWearIdentity(http.HttpContext.User);
            await mediator.Send(new AppendSosLocationsCommand(id, body, runnerId), ct);
            return Results.NoContent();
        })
            .WithSummary("Cập nhật vị trí SOS (batch)");

        // —— Organizer / Coordinator (IAM role + event-scoped check trong handler) ——
        var org = api.MapGroup("").RequireAuthorization(AuthPolicies.OrganizerOrCoordinator);

        org.MapGet("/events/{eventId:guid}/sos", async (
            Guid eventId, string? status, int? take, IMediator mediator, CancellationToken ct) =>
            Results.Ok(await mediator.Send(new ListEventSosQuery(eventId, status, take ?? 100), ct)))
            .WithSummary("Danh sách SOS theo giải")
            .WithDescription(
                "JWT role Admin | Organizer | Coordinator (không wear). "
                + "Handler: chủ giải hoặc staff Coordinator Active trên giải.");

        org.MapPost("/sos/{id:guid}/ack", async (Guid id, IMediator mediator, CancellationToken ct) =>
            Results.Ok(await mediator.Send(new AckSosCommand(id), ct)))
            .WithSummary("Ack SOS")
            .WithDescription("Role Admin|Organizer|Coordinator + quyền trên giải của SOS.");

        org.MapPost("/sos/{id:guid}/resolve", async (Guid id, IMediator mediator, CancellationToken ct) =>
            Results.Ok(await mediator.Send(new ResolveSosCommand(id), ct)))
            .WithSummary("Đóng SOS")
            .WithDescription("Role Admin|Organizer|Coordinator + quyền trên giải của SOS.");

        return app;
    }

    private static (Guid RunnerUserId, Guid LinkedDeviceId) RequireWearIdentity(ClaimsPrincipal user)
    {
        var sub = user.FindFirstValue(ClaimTypes.NameIdentifier) ?? user.FindFirstValue("sub");
        var device = user.FindFirstValue("device_id");
        if (!Guid.TryParse(sub, out var runnerId) || !Guid.TryParse(device, out var deviceId))
        {
            throw new DomainException("Wear token thiếu sub/device_id.");
        }

        return (runnerId, deviceId);
    }

    private sealed record ApproveLinkBody(string? DeviceName);
}
