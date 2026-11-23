using System.Collections.Concurrent;
using System.Threading.Channels;
using RaceDay.Modules.RaceMonitoring.Application.Abstractions;

namespace RaceDay.Modules.RaceMonitoring.Infrastructure.CutoffWarnings;

/// <summary>
/// Broadcast in one process, like the timeline broker. A slow listener loses its oldest items
/// instead of blocking the monitor; the page reloads the list when it reconnects.
/// </summary>
public sealed class InMemoryCutoffWarningBroker : ICutoffWarningBroker
{
    private const int ListenerCapacity = 256;
    private readonly ConcurrentDictionary<Guid, Subscription> _listeners = new();

    public void Publish(CutoffWarningNotification notification)
    {
        foreach (var listener in _listeners.Values)
        {
            listener.Write(notification);
        }
    }

    public ICutoffWarningSubscription Subscribe()
    {
        var subscription = new Subscription(this);
        _listeners[subscription.Id] = subscription;
        return subscription;
    }

    private sealed class Subscription(InMemoryCutoffWarningBroker owner) : ICutoffWarningSubscription
    {
        private readonly Channel<CutoffWarningNotification> _channel =
            Channel.CreateBounded<CutoffWarningNotification>(new BoundedChannelOptions(ListenerCapacity)
            {
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleReader = true,
                SingleWriter = false
            });

        public Guid Id { get; } = Guid.NewGuid();

        public ChannelReader<CutoffWarningNotification> Reader => _channel.Reader;

        public void Write(CutoffWarningNotification notification) => _channel.Writer.TryWrite(notification);

        public void Dispose()
        {
            owner._listeners.TryRemove(Id, out _);
            _channel.Writer.TryComplete();
        }
    }
}
