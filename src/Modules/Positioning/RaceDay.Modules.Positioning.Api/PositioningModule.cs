using System.Net.ServerSentEvents;
using System.Runtime.CompilerServices;
using System.Security.Claims;
using System.Threading.RateLimiting;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using RaceDay.Modules.Positioning.Application;
using RaceDay.Modules.Positioning.Contracts;
using RaceDay.Modules.Positioning.Infrastructure;
using RaceDay.Shared.Application.Abstractions;
using RaceDay.Shared.Web.Auth;

namespace RaceDay.Modules.Positioning.Api;

public static class PositioningModule
{
    public static IServiceCollection AddPositioningModule(this IServiceCollection services, IConfiguration configuration)
    {
        _ = configuration;
        services.AddScoped<PositioningAccess>();
        services.AddSingleton<IPositionTracker, CachedPositionTracker>();
        services.AddSingleton<PositionEventStream>();
        services.AddSingleton<IPositionEventPublisher>(sp => sp.GetRequiredService<PositionEventStream>());
        services.AddRateLimiter(options => options.AddPolicy("positioning", context =>
            RateLimitPartition.GetFixedWindowLimiter(context.Connection.RemoteIpAddress?.ToString() ?? "local",
                _ => new FixedWindowRateLimiterOptions { PermitLimit = 3600, Window = TimeSpan.FromMinutes(1) })));
        return services;
    }

    public static IEndpointRouteBuilder MapPositioningEndpoints(this IEndpointRouteBuilder app)
    {
        var events = app.MapGroup("/api/v1/positioning/events/{eventId:guid}")
            .WithTags("Positioning")
            .RequireAuthorization(AuthPolicies.HumanUser)
            .RequireRateLimiting("positioning");

        events.MapPost("/positions", async (Guid eventId, ReportPositionBody body, ICurrentUserAccessor user,
            ISender sender, CancellationToken ct) =>
        {
            if (user.UserId is not Guid userId) return Results.Forbid();
            var receipt = await sender.Send(new ReportPositionCommand(eventId, userId, body), ct);
            return receipt is null ? Results.Forbid() : Results.Ok(receipt);
        }).WithSummary("Nhận mẫu GPS cho giải và kiểm tra vùng hoạt động.");

        events.MapGet("/positions", async (Guid eventId, int? offset, ICurrentUserAccessor user,
            HttpContext context, ISender sender, CancellationToken ct) =>
        {
            if (user.UserId is not Guid userId) return Results.Forbid();
            if (offset < 0) return Results.BadRequest(new { error = "Offset không hợp lệ." });
            context.Response.Headers.CacheControl = "no-store";
            var positions = await sender.Send(new ListLiveStaffPositionsQuery(eventId, userId, offset ?? 0), ct);
            return positions is null ? Results.Forbid() : Results.Ok(positions);
        }).WithSummary("Vị trí GPS mới nhất của Staff và Photographer đang chia sẻ tại giải.");

        events.MapGet("/alerts", (Guid eventId, ICurrentUserAccessor user, PositioningAccess access,
            PositionEventStream stream, IServiceScopeFactory scopes, HttpContext context, CancellationToken ct) =>
            OpenStreamAsync(eventId, false, user, access, stream, scopes, context, ct))
            .WithSummary("Nhận cảnh báo rời vùng hoạt động theo thời gian thực.");

        events.MapGet("/positions/live", (Guid eventId, ICurrentUserAccessor user, PositioningAccess access,
            PositionEventStream stream, IServiceScopeFactory scopes, HttpContext context, CancellationToken ct) =>
            OpenStreamAsync(eventId, true, user, access, stream, scopes, context, ct))
            .WithSummary("Nhận vị trí GPS và cảnh báo của giải qua SSE.");

        return app;
    }

    private static async Task<IResult> OpenStreamAsync(Guid eventId, bool includePositions, ICurrentUserAccessor user,
        PositioningAccess access, PositionEventStream stream, IServiceScopeFactory scopes, HttpContext context, CancellationToken ct)
    {
        if (user.UserId is not Guid userId || !await access.CanWatchAsync(eventId, userId, ct)) return Results.Forbid();

        var exp = context.User.FindFirstValue("exp");
        var expiresAt = long.TryParse(exp, out var seconds)
            ? DateTimeOffset.FromUnixTimeSeconds(seconds)
            : DateTimeOffset.UtcNow.AddMinutes(1);
        context.Response.Headers.CacheControl = "no-store";
        context.Response.Headers["X-Accel-Buffering"] = "no";
        return Results.ServerSentEvents(WatchAsync(eventId, userId, includePositions, expiresAt, stream, scopes, ct));
    }

    private static async IAsyncEnumerable<SseItem<object?>> WatchAsync(Guid eventId, Guid userId, bool includePositions,
        DateTimeOffset expiresAt, PositionEventStream stream, IServiceScopeFactory scopes, [EnumeratorCancellation] CancellationToken ct)
    {
        // Close the stream when its access token expires.
        using var session = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var remaining = expiresAt - DateTimeOffset.UtcNow;
        if (remaining <= TimeSpan.Zero) yield break;
        session.CancelAfter(remaining);
        await using var listener = stream.ListenAsync(eventId, includePositions, session.Token).GetAsyncEnumerator();
        if (!await listener.MoveNextAsync()) yield break;

        if (includePositions)
        {
            LiveStaffPositionPage? snapshot;
            await using (var scope = scopes.CreateAsyncScope())
            {
                var sender = scope.ServiceProvider.GetRequiredService<ISender>();
                snapshot = await sender.Send(new ListLiveStaffPositionsQuery(eventId, userId, null), session.Token);
            }
            if (snapshot is null || session.IsCancellationRequested) yield break;
            yield return new SseItem<object?>(
                new LivePositionFrame<IReadOnlyList<LiveStaffPosition>>(snapshot.Items, DateTimeOffset.UtcNow), "snapshot");
        }
        else
        {
            yield return new SseItem<object?>(null, "heartbeat");
        }

        var checkAccessAt = DateTimeOffset.UtcNow.AddSeconds(5);
        while (true)
        {
            try
            {
                if (!await listener.MoveNextAsync()) yield break;
            }
            catch (OperationCanceledException) when (session.IsCancellationRequested) { yield break; }

            if (session.IsCancellationRequested) yield break;
            if (DateTimeOffset.UtcNow >= checkAccessAt)
            {
                await using var scope = scopes.CreateAsyncScope();
                var access = scope.ServiceProvider.GetRequiredService<PositioningAccess>();
                if (!await access.CanWatchAsync(eventId, userId, session.Token) || session.IsCancellationRequested) yield break;
                checkAccessAt = DateTimeOffset.UtcNow.AddSeconds(5);
            }

            var message = listener.Current;
            yield return message switch
            {
                { Alert: { } alert } => new SseItem<object?>(alert, "outside"),
                { Position: { } position } => new SseItem<object?>(new LivePositionFrame<LiveStaffPosition>(position, DateTimeOffset.UtcNow), "position"),
                _ => new SseItem<object?>(null, "heartbeat")
            };
        }
    }
}
