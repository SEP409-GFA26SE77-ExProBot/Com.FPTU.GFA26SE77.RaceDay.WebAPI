using System.IO.Pipelines;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using RaceDay.Modules.Event.Contracts.Events;
using RaceDay.Modules.RaceMonitoring.Api;
using RaceDay.Modules.RaceMonitoring.Application.Abstractions;
using RaceDay.Modules.RaceMonitoring.Application.CutoffWarnings;
using RaceDay.Modules.RaceMonitoring.Infrastructure.CutoffWarnings;
using RaceDay.Shared.Application.Abstractions;

namespace RaceDay.Modules.RaceMonitoring.UnitTests.Api;

/// <summary>
/// The admin-fe listener depends on this wire format: an SSE "event:" named after the
/// notification kind and camelCase JSON fields (bibNumber, runnerName, checkpointName, cutoffAtUtc).
/// </summary>
public sealed class CutoffWarningSseTests
{
    private static readonly TimeSpan ReadTimeout = TimeSpan.FromSeconds(5);
    private static readonly Guid AllowedEvent = Guid.NewGuid();
    private static readonly Guid OtherEvent = Guid.NewGuid();

    [Fact]
    public async Task Stream_sends_ready_then_the_raised_warning_as_a_named_camelCase_event()
    {
        await using var stream = OpenStream(admin: true);
        var ready = await stream.NextFrameAsync();
        Assert.Equal("ready", ready.Event);
        Assert.Equal("text/event-stream", stream.Context.Response.ContentType);

        var notification = Notification(AllowedEvent, "1234", "Nguyễn Văn A", "CP2 Đèo Hải Vân");
        stream.Broker.Publish(notification);
        var frame = await stream.NextFrameAsync();

        Assert.Equal(CutoffWarningNotificationKind.Raised, frame.Event);
        using var json = JsonDocument.Parse(frame.Data);
        var root = json.RootElement;
        Assert.Equal("CutoffWarningRaised", root.GetProperty("kind").GetString());
        Assert.Equal(notification.WarningId, root.GetProperty("warningId").GetGuid());
        Assert.Equal("1234", root.GetProperty("bibNumber").GetString());
        Assert.Equal("Nguyễn Văn A", root.GetProperty("runnerName").GetString());
        Assert.Equal("CP2 Đèo Hải Vân", root.GetProperty("checkpointName").GetString());
        Assert.Equal(notification.CutoffAtUtc, root.GetProperty("cutoffAtUtc").GetDateTime());
    }

    [Fact]
    public async Task Stream_only_delivers_warnings_for_events_the_viewer_may_manage()
    {
        await using var stream = OpenStream(admin: false);
        Assert.Equal("ready", (await stream.NextFrameAsync()).Event);

        stream.Broker.Publish(Notification(OtherEvent, "9999", "Other runner", "CP1"));
        stream.Broker.Publish(Notification(AllowedEvent, "1234", "Own runner", "CP1"));
        var frame = await stream.NextFrameAsync();

        using var json = JsonDocument.Parse(frame.Data);
        Assert.Equal("1234", json.RootElement.GetProperty("bibNumber").GetString());
        Assert.Equal(AllowedEvent, json.RootElement.GetProperty("eventId").GetGuid());
    }

    private static CutoffWarningNotification Notification(Guid eventId, string bib, string runner, string checkpoint)
        => new(
            CutoffWarningNotificationKind.Raised,
            Guid.NewGuid(),
            eventId,
            "Giải test",
            Guid.NewGuid(),
            bib,
            runner,
            Guid.NewGuid(),
            checkpoint,
            new DateTime(2026, 11, 23, 0, 30, 0, DateTimeKind.Utc),
            "Active",
            new DateTime(2026, 11, 23, 0, 32, 0, DateTimeKind.Utc));

    private static SseClient OpenStream(bool admin)
    {
        var userId = Guid.NewGuid();
        var sosOps = Substitute.For<IEventSosOpsAccessQuery>();
        sosOps.CanManageSosAsync(AllowedEvent, userId, Arg.Any<CancellationToken>()).Returns(true);
        sosOps.CanManageSosAsync(OtherEvent, userId, Arg.Any<CancellationToken>()).Returns(false);

        var services = new ServiceCollection();
        services.AddOptions();
        services.AddSingleton(Substitute.For<IEventOrganizerAccessQuery>());
        services.AddSingleton(sosOps);
        services.AddSingleton<ICurrentUserAccessor>(new FakeUser(userId, admin));
        services.AddScoped<CutoffWarningAccess>();
        var provider = services.BuildServiceProvider();

        var broker = new InMemoryCutoffWarningBroker();
        var pipe = new Pipe();
        var aborted = new CancellationTokenSource();
        var context = new DefaultHttpContext { RequestServices = provider, RequestAborted = aborted.Token };
        context.Response.Body = pipe.Writer.AsStream();

        var result = CutoffWarningSse.Open(
            context,
            provider.GetRequiredService<CutoffWarningAccess>(),
            broker,
            provider.GetRequiredService<IServiceScopeFactory>(),
            aborted.Token);
        var running = Task.Run(async () =>
        {
            try
            {
                await result.ExecuteAsync(context);
            }
            catch (OperationCanceledException)
            {
            }
            finally
            {
                await pipe.Writer.CompleteAsync();
            }
        });

        return new SseClient(context, broker, new StreamReader(pipe.Reader.AsStream()), aborted, running, provider);
    }

    private sealed class FakeUser(Guid id, bool admin) : ICurrentUserAccessor
    {
        public Guid? UserId => id;

        public bool IsInRole(string role) => admin && role == "Admin";
    }

    private sealed record SseFrame(string Event, string Data);

    private sealed class SseClient(
        HttpContext context,
        ICutoffWarningBroker broker,
        StreamReader reader,
        CancellationTokenSource aborted,
        Task running,
        ServiceProvider provider) : IAsyncDisposable
    {
        public HttpContext Context { get; } = context;

        public ICutoffWarningBroker Broker { get; } = broker;

        /// <summary>Reads the next frame, skipping heartbeats.</summary>
        public async Task<SseFrame> NextFrameAsync()
        {
            using var timeout = new CancellationTokenSource(ReadTimeout);
            while (true)
            {
                string? name = null;
                var data = new List<string>();
                while (await reader.ReadLineAsync(timeout.Token) is { } line)
                {
                    if (line.Length == 0)
                    {
                        break;
                    }

                    if (line.StartsWith("event: ", StringComparison.Ordinal))
                    {
                        name = line["event: ".Length..];
                    }
                    else if (line.StartsWith("data: ", StringComparison.Ordinal))
                    {
                        data.Add(line["data: ".Length..]);
                    }
                }

                if (name is null)
                {
                    throw new InvalidOperationException("Stream ended without a frame.");
                }

                if (name != CutoffWarningSse.HeartbeatEventName)
                {
                    return new SseFrame(name, string.Join('\n', data));
                }
            }
        }

        public async ValueTask DisposeAsync()
        {
            await aborted.CancelAsync();
            await running.WaitAsync(ReadTimeout);
            reader.Dispose();
            aborted.Dispose();
            await provider.DisposeAsync();
        }
    }
}
