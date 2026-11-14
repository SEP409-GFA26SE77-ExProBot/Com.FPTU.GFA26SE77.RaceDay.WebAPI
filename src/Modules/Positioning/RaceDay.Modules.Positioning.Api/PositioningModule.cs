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
        services.AddSingleton<PositionAlertStream>();
        services.AddSingleton<IPositionAlertPublisher>(sp => sp.GetRequiredService<PositionAlertStream>());
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

        events.MapGet("/alerts", async (Guid eventId, ICurrentUserAccessor user, PositioningAccess access,
            PositionAlertStream stream, IServiceScopeFactory scopes, HttpContext context, CancellationToken ct) =>
        {
            if (user.UserId is not Guid userId || !await access.CanWatchAsync(eventId, userId, ct))
            {
                return Results.Forbid();
            }
            var exp = context.User.FindFirstValue("exp");
            var expiresAt = long.TryParse(exp, out var seconds)
                ? DateTimeOffset.FromUnixTimeSeconds(seconds)
                : DateTimeOffset.UtcNow.AddMinutes(1);
            return Results.ServerSentEvents(WatchAsync(eventId, userId, expiresAt, stream, scopes, ct));
        }).WithSummary("Nhận cảnh báo rời vùng hoạt động theo thời gian thực.");

        return app;
    }

    private static async IAsyncEnumerable<SseItem<PositionAlert?>> WatchAsync(Guid eventId, Guid userId, DateTimeOffset expiresAt,
        PositionAlertStream stream, IServiceScopeFactory scopes, [EnumeratorCancellation] CancellationToken ct)
    {
        // Close the stream when its access token expires.
        using var session = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var remaining = expiresAt - DateTimeOffset.UtcNow;
        if (remaining <= TimeSpan.Zero) yield break;
        session.CancelAfter(remaining);
        await using var listener = stream.ListenAsync(eventId, session.Token).GetAsyncEnumerator();
        while (true)
        {
            try
            {
                if (!await listener.MoveNextAsync()) yield break;
            }
            catch (OperationCanceledException) when (session.IsCancellationRequested) { yield break; }

            await using (var scope = scopes.CreateAsyncScope())
            {
                var access = scope.ServiceProvider.GetRequiredService<PositioningAccess>();
                if (!await access.CanWatchAsync(eventId, userId, ct)) yield break;
            }

            var alert = listener.Current;
            yield return new SseItem<PositionAlert?>(alert, alert is null ? "heartbeat" : "outside");
        }
    }
}
