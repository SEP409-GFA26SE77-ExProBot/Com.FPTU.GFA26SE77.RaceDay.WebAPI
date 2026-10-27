using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RaceDay.Modules.RaceMonitoring.Application.Abstractions;
using RaceDay.Modules.RaceMonitoring.Application.RaceEventLogs;
using RaceDay.Shared.Domain.Exceptions;

namespace RaceDay.Modules.RaceMonitoring.Api;

public static class RaceEventLogSse
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static async Task WriteAsync(
        HttpContext httpContext,
        Guid eventId,
        IServiceScopeFactory scopeFactory,
        IRaceEventLogBroker broker,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        await using (var scope = scopeFactory.CreateAsyncScope())
        {
            var mediator = scope.ServiceProvider.GetRequiredService<MediatR.IMediator>();
            await mediator.Send(new EnsureRaceTimelineAccessQuery(eventId), cancellationToken);
        }

        var lastSequence = ReadLastSequence(httpContext);
        var response = httpContext.Response;
        response.StatusCode = StatusCodes.Status200OK;
        response.ContentType = "text/event-stream";
        response.Headers.CacheControl = "no-cache";
        response.Headers.Append("X-Accel-Buffering", "no");
        httpContext.Features.Get<IHttpResponseBodyFeature>()?.DisableBuffering();

        var subscription = broker.Subscribe(eventId);
        var seen = new HashSet<Guid>();
        try
        {
            await response.StartAsync(cancellationToken);
            if (lastSequence is long cursor)
            {
                var recordedAfterUtc = DateTime.UtcNow - RaceEventLogStreamPolicy.ReplayOverlap;
                await using var scope = scopeFactory.CreateAsyncScope();
                var logs = scope.ServiceProvider.GetRequiredService<IRaceEventLogRepository>();
                await foreach (var log in logs.ReplayAsync(eventId, cursor, recordedAfterUtc, cancellationToken))
                {
                    var notification = RaceEventLogMapper.ToNotification(log);
                    if (!seen.Add(notification.Id))
                    {
                        continue;
                    }

                    await WriteLogAsync(response.Body, notification, cancellationToken);
                }
            }

            var nextHeartbeat = DateTime.UtcNow + RaceEventLogStreamPolicy.HeartbeatInterval;
            while (!cancellationToken.IsCancellationRequested)
            {
                var remaining = nextHeartbeat - DateTime.UtcNow;
                if (remaining <= TimeSpan.Zero)
                {
                    if (!await StillAllowedAsync(scopeFactory, eventId, cancellationToken))
                    {
                        await WriteControlAsync(
                            response.Body,
                            RaceEventLogStreamPolicy.AccessRevokedEventName,
                            new { eventId },
                            cancellationToken);
                        break;
                    }

                    await WriteCommentAsync(response.Body, "ping", cancellationToken);
                    nextHeartbeat = DateTime.UtcNow + RaceEventLogStreamPolicy.HeartbeatInterval;
                    continue;
                }

                using var wait = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                wait.CancelAfter(remaining);
                try
                {
                    var hasItem = await subscription.WaitToReadAsync(wait.Token);
                    if (!hasItem)
                    {
                        if (subscription.IsOverrun)
                        {
                            await WriteControlAsync(
                                response.Body,
                                RaceEventLogStreamPolicy.OverrunEventName,
                                new { eventId },
                                cancellationToken);
                        }

                        break;
                    }

                    while (subscription.TryRead(out var notification))
                    {
                        if (notification.EventId != eventId || !seen.Add(notification.Id))
                        {
                            continue;
                        }

                        await WriteLogAsync(response.Body, notification, cancellationToken);
                    }
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    // Hết khoảng heartbeat, vòng sau kiểm tra quyền và gửi comment.
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Client ngắt kết nối.
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Stream timeline của giải {EventId} kết thúc vì lỗi.", eventId);
        }
        finally
        {
            subscription.Dispose();
        }
    }

    private static long? ReadLastSequence(HttpContext httpContext)
    {
        if (!httpContext.Request.Headers.TryGetValue("Last-Event-ID", out var raw)
            || string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        if (!long.TryParse(raw.ToString(), out var sequence) || sequence < 0)
        {
            throw new DomainException("Last-Event-ID không hợp lệ.");
        }

        return sequence;
    }

    private static async Task<bool> StillAllowedAsync(
        IServiceScopeFactory scopeFactory,
        Guid eventId,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var mediator = scope.ServiceProvider.GetRequiredService<MediatR.IMediator>();
            await mediator.Send(new EnsureRaceTimelineAccessQuery(eventId), cancellationToken);
            return true;
        }
        catch (DomainException)
        {
            return false;
        }
    }

    private static Task WriteLogAsync(
        Stream body,
        RaceEventLogNotification notification,
        CancellationToken cancellationToken)
        => WriteFrameAsync(
            body,
            RaceEventLogStreamPolicy.CreatedEventName,
            notification.Sequence.ToString(),
            JsonSerializer.Serialize(notification, JsonOptions),
            cancellationToken);

    private static Task WriteControlAsync(
        Stream body,
        string eventName,
        object payload,
        CancellationToken cancellationToken)
        => WriteFrameAsync(body, eventName, null, JsonSerializer.Serialize(payload, JsonOptions), cancellationToken);

    private static async Task WriteFrameAsync(
        Stream body,
        string eventName,
        string? id,
        string data,
        CancellationToken cancellationToken)
    {
        var frame = id is null
            ? $"event: {eventName}\ndata: {data}\n\n"
            : $"id: {id}\nevent: {eventName}\ndata: {data}\n\n";
        await body.WriteAsync(Encoding.UTF8.GetBytes(frame), cancellationToken);
        await body.FlushAsync(cancellationToken);
    }

    private static async Task WriteCommentAsync(Stream body, string comment, CancellationToken cancellationToken)
    {
        await body.WriteAsync(Encoding.UTF8.GetBytes($": {comment}\n\n"), cancellationToken);
        await body.FlushAsync(cancellationToken);
    }
}
