using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using RaceDay.Modules.Map.Application.Courses;
using RaceDay.Modules.Map.Infrastructure;
using RaceDay.Shared.Web.Auth;
using RaceDay.Shared.Web.OpenApi;

namespace RaceDay.Modules.Map.Api;

public static class MapModule
{
    public static IServiceCollection AddMapModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(typeof(GetCourseQuery).Assembly));
        services.AddValidatorsFromAssembly(typeof(UpsertCourseCommandValidator).Assembly);
        services.AddMapInfrastructure(configuration);
        return services;
    }

    public static IEndpointRouteBuilder MapMapEndpoints(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api/v1/map")
            .WithTags(ApiTags.Map)
            .RequireRateLimiting("public");

        api.MapGet("/status", () => Results.Ok(new { module = "map", status = "ready" }))
            .WithSummary("Trạng thái module Map");

        // Organizer / admin / public (approved+) course
        api.MapGet("/events/{eventId:guid}/distances/{distanceId:guid}/course", async (
            Guid eventId,
            Guid distanceId,
            IMediator mediator,
            CancellationToken ct) =>
            Results.Ok(await mediator.Send(new GetCourseQuery(eventId, distanceId), ct)))
            .WithSummary("GET course theo (event, distance) — FE organizer setup map + simulator")
            .WithDescription(
                "Chưa có course → trả empty (polyline []). Organizer/Admin luôn được đọc; "
                + "user khác chỉ khi giải Approved / RegistrationOpen / Closed / Completed.");

        api.MapPut("/events/{eventId:guid}/distances/{distanceId:guid}/course", async (
            Guid eventId,
            Guid distanceId,
            UpsertCourseRequest body,
            IMediator mediator,
            CancellationToken ct) =>
            Results.Ok(await mediator.Send(new UpsertCourseCommand(eventId, distanceId, body), ct)))
            .RequireAuthorization(AuthPolicies.PublicUser)
            .WithSummary("Lưu course (upsert) — chỉ organizer của giải hoặc Admin");

        api.MapGet("/events/{eventId:guid}/courses/ready", async (
            Guid eventId,
            IMediator mediator,
            CancellationToken ct) =>
            Results.Ok(await mediator.Send(new ListCourseReadyQuery(eventId), ct)))
            .RequireAuthorization(AuthPolicies.PublicUser)
            .WithSummary("Map distanceId → đã có route sẵn (polyline ≥ 2)");

        // FE True race-map.ts legacy paths under /event/organizer/...
        var feCompat = app.MapGroup("/api/v1/event/organizer")
            .WithTags(ApiTags.Map)
            .RequireRateLimiting("public")
            .RequireAuthorization(AuthPolicies.PublicUser);

        feCompat.MapGet("/{eventId:guid}/distances/{distanceId:guid}/course", async (
            Guid eventId,
            Guid distanceId,
            IMediator mediator,
            CancellationToken ct) =>
            Results.Ok(await mediator.Send(new GetCourseQuery(eventId, distanceId), ct)))
            .WithSummary("[FE compat] GET course — alias Map API");

        feCompat.MapPut("/{eventId:guid}/distances/{distanceId:guid}/course", async (
            Guid eventId,
            Guid distanceId,
            UpsertCourseRequest body,
            IMediator mediator,
            CancellationToken ct) =>
            Results.Ok(await mediator.Send(new UpsertCourseCommand(eventId, distanceId, body), ct)))
            .WithSummary("[FE compat] PUT course — alias Map API");

        return app;
    }
}
