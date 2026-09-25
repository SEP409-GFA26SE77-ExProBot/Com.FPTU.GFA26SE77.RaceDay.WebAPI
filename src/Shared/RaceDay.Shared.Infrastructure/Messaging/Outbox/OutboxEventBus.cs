using System.Text.Json;
using Microsoft.Extensions.Logging;
using RaceDay.Shared.Application.Abstractions;

namespace RaceDay.Shared.Infrastructure.Messaging.Outbox;

/// <summary>
/// Enqueues integration events into the transactional outbox (same DbContext as the module UoW).
/// Callers must SaveChanges after Publish so business data + outbox commit together.
/// </summary>
public sealed class OutboxEventBus(
    IOutboxStore outboxStore,
    ILogger<OutboxEventBus> logger) : IEventBus
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public Task PublishAsync<TEvent>(TEvent @event, CancellationToken cancellationToken = default)
        where TEvent : class
    {
        if (@event is not IIntegrationEvent integrationEvent)
        {
            logger.LogDebug("Skipping non-integration event {EventType}", typeof(TEvent).Name);
            return Task.CompletedTask;
        }

        logger.LogInformation("Enqueue outbox event {EventType} ({EventId})", typeof(TEvent).Name, integrationEvent.EventId);
        return outboxStore.AddAsync(integrationEvent, cancellationToken);
    }

    public static string Serialize(IIntegrationEvent integrationEvent) =>
        JsonSerializer.Serialize(integrationEvent, integrationEvent.GetType(), JsonOptions);
}
