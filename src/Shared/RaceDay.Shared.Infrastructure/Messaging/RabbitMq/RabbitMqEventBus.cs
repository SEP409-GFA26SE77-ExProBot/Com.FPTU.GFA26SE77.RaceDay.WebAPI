using RaceDay.Shared.Application.Abstractions;
using Microsoft.Extensions.Logging;

namespace RaceDay.Shared.Infrastructure.Messaging.RabbitMq;

/// <summary>
/// Scaffold publisher. Wire a real RabbitMQ connection in production configuration.
/// </summary>
public sealed class RabbitMqEventBus(ILogger<RabbitMqEventBus> logger) : IEventBus
{
    public Task PublishAsync<TEvent>(TEvent @event, CancellationToken cancellationToken = default)
        where TEvent : class
    {
        logger.LogInformation("Event published (scaffold): {EventType}", typeof(TEvent).Name);
        // TODO: serialize + publish to exchange/queue via RabbitMQ.Client
        return Task.CompletedTask;
    }
}
