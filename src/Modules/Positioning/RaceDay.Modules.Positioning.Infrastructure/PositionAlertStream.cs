using System.Runtime.CompilerServices;
using System.Threading.Channels;
using RaceDay.Modules.Positioning.Application;
using RaceDay.Modules.Positioning.Contracts;

namespace RaceDay.Modules.Positioning.Infrastructure;

public sealed class PositionAlertStream : IPositionAlertPublisher
{
    private readonly object _gate = new();
    private readonly Dictionary<Guid, HashSet<ChannelWriter<PositionAlert>>> _listeners = new();

    public void Publish(PositionAlert alert)
    {
        lock (_gate)
        {
            if (!_listeners.TryGetValue(alert.EventId, out var listeners)) return;
            foreach (var listener in listeners) listener.TryWrite(alert);
        }
    }

    public async IAsyncEnumerable<PositionAlert?> ListenAsync(Guid eventId, [EnumeratorCancellation] CancellationToken ct)
    {
        var channel = Channel.CreateBounded<PositionAlert>(new BoundedChannelOptions(32)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = true
        });
        lock (_gate)
        {
            if (!_listeners.TryGetValue(eventId, out var listeners))
            {
                listeners = new HashSet<ChannelWriter<PositionAlert>>();
                _listeners.Add(eventId, listeners);
            }
            listeners.Add(channel.Writer);
        }

        try
        {
            yield return null;
            while (!ct.IsCancellationRequested)
            {
                using var heartbeat = CancellationTokenSource.CreateLinkedTokenSource(ct);
                heartbeat.CancelAfter(TimeSpan.FromSeconds(15));
                PositionAlert? alert = null;
                try
                {
                    alert = await channel.Reader.ReadAsync(heartbeat.Token);
                }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                {
                    // Keep idle proxies connected until the next alert.
                }
                yield return alert;
            }
        }
        finally
        {
            lock (_gate)
            {
                var listeners = _listeners[eventId];
                listeners.Remove(channel.Writer);
                if (listeners.Count == 0) _listeners.Remove(eventId);
            }
        }
    }
}
