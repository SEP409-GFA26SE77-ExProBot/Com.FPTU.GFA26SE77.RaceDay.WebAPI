using MediatR;
using Microsoft.Extensions.Logging;
using RaceDay.Modules.Iam.Contracts.Events;
using RaceDay.Modules.Event.Contracts.Events;
using RaceDay.Shared.Application.Abstractions;
using RaceDay.Shared.Application.Messaging;

namespace RaceDay.Modules.Event.Application.Events.Create;

/// <summary>
/// Handles RaceEventCreatedIntegrationEvent by publishing a signal to IAM module
/// to assign the organizer role to the event creator.
/// 
/// This handler encapsulates the Event module responsibility:
/// - Detects when an event is created
/// - Publishes an event that IAM module listens to
/// 
/// The actual role assignment happens in IAM module (OrganizerRoleAssignmentHandler).
/// This maintains clean module boundaries per AGENTS.md Non-negotiable #4:
/// "Modules talk via Contracts + events, never via another module's Infrastructure/Domain."
/// </summary>
public sealed class RaceEventCreatedEventHandler(
    IEventBus eventBus,
    ILogger<RaceEventCreatedEventHandler> logger) : INotificationHandler<IntegrationEventOccurred>
{
    public async Task Handle(IntegrationEventOccurred notification, CancellationToken cancellationToken)
    {
        if (notification.Event is not RaceEventCreatedIntegrationEvent created)
        {
            return;
        }

        try
        {
            // Publish event to IAM module - request to assign organizer role
            await eventBus.PublishAsync(
                new OrganizerRoleRequiredIntegrationEvent(created.OrganizerUserId),
                cancellationToken);

            logger.LogInformation(
                "Published OrganizerRoleRequired event for user {UserId} after creating race event {EventId} '{EventName}'",
                created.OrganizerUserId,
                created.RaceEventId,
                created.Name);
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Error publishing OrganizerRoleRequired event for user {UserId} on event {EventId}",
                created.OrganizerUserId,
                created.RaceEventId);
            // Don't rethrow - event creation succeeded, notification is best-effort
        }
    }
}
