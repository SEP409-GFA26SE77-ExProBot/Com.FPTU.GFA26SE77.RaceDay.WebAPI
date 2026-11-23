using System.Net.ServerSentEvents;
using System.Runtime.CompilerServices;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using RaceDay.Modules.RaceMonitoring.Application.Abstractions;
using RaceDay.Modules.RaceMonitoring.Application.CutoffWarnings;

namespace RaceDay.Modules.RaceMonitoring.Api;

/// <summary>
/// One stream per signed-in user across every event they may manage. Each notification is
/// re-checked against event access (cached briefly), so a user only hears about their own events.
/// </summary>
public static class CutoffWarningSse
{
    public const string ReadyEventName = "ready";
    public const string HeartbeatEventName = "heartbeat";
    private static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan AccessCacheTtl = TimeSpan.FromSeconds(30);

    public static IResult Open(
        HttpContext context,
        CutoffWarningAccess access,
        ICutoffWarningBroker broker,
        IServiceScopeFactory scopes,
        CancellationToken cancellationToken)
    {
        var viewer = access.CurrentViewer();
        var expiresAt = long.TryParse(context.User.FindFirstValue("exp"), out var seconds)
            ? DateTimeOffset.FromUnixTimeSeconds(seconds)
            : DateTimeOffset.UtcNow.AddMinutes(5);
        context.Response.Headers.CacheControl = "no-store";
        context.Response.Headers["X-Accel-Buffering"] = "no";
        return Results.ServerSentEvents(WatchAsync(viewer, expiresAt, broker, scopes, cancellationToken));
    }

    private static async IAsyncEnumerable<SseItem<object?>> WatchAsync(
        CutoffWarningViewer viewer,
        DateTimeOffset expiresAt,
        ICutoffWarningBroker broker,
        IServiceScopeFactory scopes,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var remaining = expiresAt - DateTimeOffset.UtcNow;
        if (remaining <= TimeSpan.Zero)
        {
            yield break;
        }

        // Close the stream when the access token expires; the browser reconnects with a fresh cookie.
        using var session = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        session.CancelAfter(remaining);
        using var subscription = broker.Subscribe();
        var allowed = new Dictionary<Guid, (bool Allowed, DateTime CheckedAtUtc)>();
        yield return new SseItem<object?>(new { serverNowUtc = DateTime.UtcNow }, ReadyEventName);

        while (!session.IsCancellationRequested)
        {
            var timedOut = false;
            var hasItem = false;
            using (var wait = CancellationTokenSource.CreateLinkedTokenSource(session.Token))
            {
                wait.CancelAfter(HeartbeatInterval);
                try
                {
                    hasItem = await subscription.Reader.WaitToReadAsync(wait.Token);
                }
                catch (OperationCanceledException)
                {
                    timedOut = !session.IsCancellationRequested;
                }
            }

            if (session.IsCancellationRequested)
            {
                yield break;
            }

            if (timedOut)
            {
                yield return new SseItem<object?>(null, HeartbeatEventName);
                continue;
            }

            if (!hasItem)
            {
                yield break;
            }

            while (subscription.Reader.TryRead(out var notification))
            {
                if (await CanSeeAsync(notification.EventId, viewer, allowed, scopes, session.Token))
                {
                    yield return new SseItem<object?>(notification, notification.Kind);
                }
            }
        }
    }

    private static async Task<bool> CanSeeAsync(
        Guid eventId,
        CutoffWarningViewer viewer,
        Dictionary<Guid, (bool Allowed, DateTime CheckedAtUtc)> cache,
        IServiceScopeFactory scopes,
        CancellationToken cancellationToken)
    {
        if (viewer.IsAdmin)
        {
            return true;
        }

        var now = DateTime.UtcNow;
        if (cache.TryGetValue(eventId, out var hit) && now - hit.CheckedAtUtc < AccessCacheTtl)
        {
            return hit.Allowed;
        }

        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var access = scope.ServiceProvider.GetRequiredService<CutoffWarningAccess>();
            var allowed = await access.CanManageAsync(eventId, viewer, cancellationToken);
            cache[eventId] = (allowed, now);
            return allowed;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Fail closed: skip this item rather than leak another event's runners.
            return false;
        }
    }
}
