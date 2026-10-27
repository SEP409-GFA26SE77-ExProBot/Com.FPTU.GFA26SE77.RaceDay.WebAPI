using RaceDay.Modules.RaceMonitoring.Application.Abstractions;
using RaceDay.Modules.RaceMonitoring.Infrastructure.RaceEventLogs;

namespace RaceDay.Modules.RaceMonitoring.UnitTests.Application;

public class RaceEventLogBrokerTests
{
    [Fact]
    public void Two_Subscribers_Of_The_Same_Event_Both_Receive()
    {
        var broker = new InMemoryRaceEventLogBroker();
        var eventId = Guid.NewGuid();
        using var first = broker.Subscribe(eventId);
        using var second = broker.Subscribe(eventId);
        var notification = Notice(eventId);

        broker.Publish(notification);

        Assert.True(first.TryRead(out var left));
        Assert.True(second.TryRead(out var right));
        Assert.Equal(notification.Id, left.Id);
        Assert.Equal(notification.Id, right.Id);
    }

    [Fact]
    public void Another_Event_Does_Not_Receive()
    {
        var broker = new InMemoryRaceEventLogBroker();
        using var subscription = broker.Subscribe(Guid.NewGuid());

        broker.Publish(Notice(Guid.NewGuid()));

        Assert.False(subscription.TryRead(out _));
    }

    [Fact]
    public void Dispose_Stops_Delivery()
    {
        var broker = new InMemoryRaceEventLogBroker();
        var eventId = Guid.NewGuid();
        var subscription = broker.Subscribe(eventId);
        subscription.Dispose();

        broker.Publish(Notice(eventId));

        Assert.False(subscription.TryRead(out _));
    }

    [Fact]
    public void Full_Subscriber_Does_Not_Block_Publish()
    {
        var broker = new InMemoryRaceEventLogBroker(capacity: 1);
        var eventId = Guid.NewGuid();
        using var slow = broker.Subscribe(eventId);
        using var other = broker.Subscribe(eventId);

        var started = DateTime.UtcNow;
        broker.Publish(Notice(eventId, Guid.NewGuid()));
        broker.Publish(Notice(eventId, Guid.NewGuid()));
        var elapsed = DateTime.UtcNow - started;

        Assert.True(elapsed < TimeSpan.FromSeconds(1));
        Assert.True(slow.IsOverrun);
        Assert.True(other.TryRead(out _));
    }

    private static RaceEventLogNotification Notice(Guid eventId, Guid? id = null) => new(
        id ?? Guid.NewGuid(),
        1,
        eventId,
        Guid.NewGuid(),
        "RunnerCheckpointPassed",
        DateTime.UtcNow,
        DateTime.UtcNow,
        "1024",
        Guid.NewGuid(),
        Guid.NewGuid(),
        "CheckpointScan",
        Guid.NewGuid(),
        "Manual");
}
