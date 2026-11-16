using System.Runtime.CompilerServices;
using System.Threading.Channels;
using RaceDay.Modules.Positioning.Application;
using RaceDay.Modules.Positioning.Contracts;

namespace RaceDay.Modules.Positioning.Infrastructure;

public sealed record PositionStreamEvent(PositionAlert? Alert = null, LiveStaffPosition? Position = null);

public sealed class PositionEventStream : IPositionEventPublisher
{
    private readonly object _gate = new();
    private readonly Dictionary<Guid, HashSet<Listener>> _listeners = new();

    public void Publish(PositionAlert alert) => Publish(alert.EventId, new PositionStreamEvent(Alert: alert));

    public void Publish(Guid eventId, LiveStaffPosition position) => Publish(eventId, new PositionStreamEvent(Position: position));

    private void Publish(Guid eventId, PositionStreamEvent message)
    {
        lock (_gate)
        {
            if (!_listeners.TryGetValue(eventId, out var listeners)) return;
            foreach (var listener in listeners)
            {
                if (message.Alert is not null || listener.IncludePositions) listener.Writer.TryWrite(message);
            }
        }
    }

    public async IAsyncEnumerable<PositionStreamEvent?> ListenAsync(Guid eventId, bool includePositions, [EnumeratorCancellation] CancellationToken ct)
    {
        var channel = Channel.CreateBounded<PositionStreamEvent>(new BoundedChannelOptions(128)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = true
        });
        var listener = new Listener(channel.Writer, includePositions);
        lock (_gate)
        {
            if (!_listeners.TryGetValue(eventId, out var listeners))
            {
                listeners = [];
                _listeners.Add(eventId, listeners);
            }
            listeners.Add(listener);
        }

        try
        {
            yield return null;
            while (!ct.IsCancellationRequested)
            {
                using var heartbeat = CancellationTokenSource.CreateLinkedTokenSource(ct);
                heartbeat.CancelAfter(TimeSpan.FromSeconds(15));
                PositionStreamEvent? message = null;
                try
                {
                    message = await channel.Reader.ReadAsync(heartbeat.Token);
                }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                {
                    // Keep idle proxies connected until the next alert.
                }
                yield return message;
            }
        }
        finally
        {
            lock (_gate)
            {
                var listeners = _listeners[eventId];
                listeners.Remove(listener);
                if (listeners.Count == 0) _listeners.Remove(eventId);
            }
        }
    }

    private sealed record Listener(ChannelWriter<PositionStreamEvent> Writer, bool IncludePositions);
}
