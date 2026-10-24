using System.Text.Json;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RaceDay.Shared.Application.Abstractions;
using RaceDay.Shared.Application.Messaging;
using RaceDay.Shared.Infrastructure.Messaging.RabbitMq;

namespace RaceDay.Shared.Infrastructure.Messaging;

public sealed class MediatorEventBus(
    IServiceScopeFactory scopeFactory,
    IRabbitMqPublisher rabbitMqPublisher,
    ILogger<MediatorEventBus> logger) : IEventBus
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task PublishAsync<TEvent>(TEvent @event, CancellationToken cancellationToken = default)
        where TEvent : class
    {
        logger.LogInformation("Publishing integration event {EventType}", typeof(TEvent).Name);

        if (@event is not IIntegrationEvent integrationEvent)
        {
            return;
        }

        using var scope = scopeFactory.CreateScope();
        var publisher = scope.ServiceProvider.GetRequiredService<IPublisher>();
        await publisher.Publish(new IntegrationEventOccurred(integrationEvent), cancellationToken);

        // Mirror OutboxDispatcher: fan-out to RabbitMQ when enabled (best-effort).
        if (rabbitMqPublisher.IsEnabled)
        {
            try
            {
                var payload = JsonSerializer.Serialize(integrationEvent, integrationEvent.GetType(), JsonOptions);
                await rabbitMqPublisher.PublishAsync(
                    integrationEvent.GetType().FullName ?? integrationEvent.GetType().Name,
                    payload,
                    cancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogWarning(
                    ex,
                    "RabbitMQ publish failed for {EventType}; in-process handlers already ran",
                    integrationEvent.GetType().Name);
            }
        }
    }
}
