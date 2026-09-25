using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RaceDay.Shared.Application.Abstractions;
using RaceDay.Shared.Application.Messaging;

namespace RaceDay.Shared.Infrastructure.Messaging;

public sealed class MediatorEventBus(
    IServiceScopeFactory scopeFactory,
    ILogger<MediatorEventBus> logger) : IEventBus
{
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
    }
}
