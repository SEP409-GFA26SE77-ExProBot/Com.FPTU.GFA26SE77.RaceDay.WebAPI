using System.Text.Json;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RaceDay.Shared.Application.Abstractions;
using RaceDay.Shared.Application.Messaging;
using RaceDay.Shared.Infrastructure.Messaging.RabbitMq;

namespace RaceDay.Shared.Infrastructure.Messaging.Outbox;

/// <summary>
/// Polls the outbox, dispatches in-process (MediatR) for module handlers, and publishes to RabbitMQ when enabled.
/// </summary>
public sealed class OutboxDispatcher(
    IServiceScopeFactory scopeFactory,
    IRabbitMqPublisher rabbitMqPublisher,
    ILogger<OutboxDispatcher> logger) : BackgroundService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation(
            "Outbox dispatcher started (RabbitMQ {Status})",
            rabbitMqPublisher.IsEnabled ? "enabled" : "disabled");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessBatchAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Outbox dispatcher loop failed");
            }

            await Task.Delay(PollInterval, stoppingToken);
        }
    }

    private async Task ProcessBatchAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var outbox = scope.ServiceProvider.GetService<IOutboxStore>();
        if (outbox is null)
        {
            return;
        }

        var pending = await outbox.GetPendingAsync(20, cancellationToken);
        if (pending.Count == 0)
        {
            return;
        }

        var publisher = scope.ServiceProvider.GetRequiredService<IPublisher>();

        foreach (var message in pending)
        {
            try
            {
                var integrationEvent = Deserialize(message);
                if (integrationEvent is null)
                {
                    logger.LogError("Cannot deserialize outbox message {Id} ({EventType})", message.Id, message.EventType);
                    await outbox.MarkProcessedAsync(message.Id, cancellationToken);
                    continue;
                }

                await publisher.Publish(new IntegrationEventOccurred(integrationEvent), cancellationToken);

                if (rabbitMqPublisher.IsEnabled)
                {
                    try
                    {
                        await rabbitMqPublisher.PublishAsync(message.EventType, message.Payload, cancellationToken);
                    }
                    catch (Exception ex)
                    {
                        logger.LogWarning(
                            ex,
                            "RabbitMQ publish failed for outbox {Id}; in-process handlers already ran",
                            message.Id);
                    }
                }

                await outbox.MarkProcessedAsync(message.Id, cancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed processing outbox message {Id} ({EventType})", message.Id, message.EventType);
                // leave unprocessed for retry
            }
        }
    }

    private static IIntegrationEvent? Deserialize(OutboxMessageData message)
    {
        var type = ResolveEventType(message.EventType);
        if (type is null || !typeof(IIntegrationEvent).IsAssignableFrom(type))
        {
            return null;
        }

        return JsonSerializer.Deserialize(message.Payload, type, JsonOptions) as IIntegrationEvent;
    }

    private static Type? ResolveEventType(string eventType)
    {
        var direct = Type.GetType(eventType, throwOnError: false);
        if (direct is not null)
        {
            return direct;
        }

        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            var type = assembly.GetType(eventType, throwOnError: false);
            if (type is not null)
            {
                return type;
            }

            type = assembly.GetTypes().FirstOrDefault(t => t.FullName == eventType || t.Name == eventType);
            if (type is not null)
            {
                return type;
            }
        }

        return null;
    }
}
