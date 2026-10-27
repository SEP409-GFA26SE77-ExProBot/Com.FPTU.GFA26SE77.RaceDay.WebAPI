using System.Collections.Concurrent;
using System.Threading.Channels;
using RaceDay.Modules.RaceMonitoring.Application.Abstractions;
using RaceDay.Modules.RaceMonitoring.Application.RaceEventLogs;

namespace RaceDay.Modules.RaceMonitoring.Infrastructure.RaceEventLogs;

/// <summary>
/// Broadcast trong một process. Subscriber chậm bị đóng, publisher không chờ.
/// </summary>
public sealed class InMemoryRaceEventLogBroker : IRaceEventLogBroker
{
    private readonly int _capacity;
    private readonly ConcurrentDictionary<Guid, ConcurrentDictionary<Guid, Subscription>> _events = new();

    public InMemoryRaceEventLogBroker()
        : this(RaceEventLogStreamPolicy.SubscriberCapacity)
    {
    }

    public InMemoryRaceEventLogBroker(int capacity)
    {
        _capacity = capacity < 1 ? 1 : capacity;
    }

    public void Publish(RaceEventLogNotification notification)
    {
        if (!_events.TryGetValue(notification.EventId, out var subscribers))
        {
            return;
        }

        foreach (var subscription in subscribers.Values)
        {
            if (subscription.TryWrite(notification))
            {
                continue;
            }

            subscription.MarkOverrun();
            subscribers.TryRemove(subscription.Id, out _);
        }
    }

    public IRaceEventLogSubscription Subscribe(Guid eventId)
    {
        while (true)
        {
            var subscribers = _events.GetOrAdd(eventId, _ => new ConcurrentDictionary<Guid, Subscription>());
            var subscription = new Subscription(_capacity);
            subscription.Bind(() => Remove(eventId, subscription.Id));
            if (!subscribers.TryAdd(subscription.Id, subscription))
            {
                continue;
            }

            if (_events.TryGetValue(eventId, out var current) && ReferenceEquals(current, subscribers))
            {
                return subscription;
            }

            subscribers.TryRemove(subscription.Id, out _);
        }
    }

    private void Remove(Guid eventId, Guid subscriptionId)
    {
        if (!_events.TryGetValue(eventId, out var subscribers))
        {
            return;
        }

        subscribers.TryRemove(subscriptionId, out _);
        if (subscribers.IsEmpty)
        {
            _events.TryRemove(new KeyValuePair<Guid, ConcurrentDictionary<Guid, Subscription>>(eventId, subscribers));
        }
    }

    private sealed class Subscription : IRaceEventLogSubscription
    {
        private readonly Channel<RaceEventLogNotification> _channel;
        private int _end;
        private int _disposed;

        public Subscription(int capacity)
        {
            Id = Guid.NewGuid();
            // Wait + TryWrite: đầy thì trả false ngay, không chặn publisher.
            _channel = Channel.CreateBounded<RaceEventLogNotification>(new BoundedChannelOptions(capacity)
            {
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = true,
                SingleWriter = false
            });
        }

        public Guid Id { get; }

        private Action? _removed;

        public bool IsOverrun => Volatile.Read(ref _end) == 1;

        public void Bind(Action removed) => _removed = removed;

        public bool TryWrite(RaceEventLogNotification notification) => _channel.Writer.TryWrite(notification);

        public void MarkOverrun()
        {
            if (Interlocked.CompareExchange(ref _end, 1, 0) == 0)
            {
                _channel.Writer.TryComplete();
                _removed?.Invoke();
            }
        }

        public ValueTask<bool> WaitToReadAsync(CancellationToken cancellationToken)
            => _channel.Reader.WaitToReadAsync(cancellationToken);

        public bool TryRead(out RaceEventLogNotification notification)
            => _channel.Reader.TryRead(out notification!);

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 1)
            {
                return;
            }

            _removed?.Invoke();
            _channel.Writer.TryComplete();
        }
    }
}
