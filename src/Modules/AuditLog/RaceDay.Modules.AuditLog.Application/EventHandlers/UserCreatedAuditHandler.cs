using MediatR;
using RaceDay.Modules.AuditLog.Application.Abstractions;
using RaceDay.Modules.AuditLog.Domain.AuditEntries;
using RaceDay.Modules.Iam.Contracts.Events;

namespace RaceDay.Modules.AuditLog.Application.EventHandlers;

public sealed class UserCreatedAuditHandler(IAuditEntryRepository repository)
    : INotificationHandler<UserCreatedNotification>
{
    public async Task Handle(UserCreatedNotification notification, CancellationToken cancellationToken)
    {
        var entry = AuditEntry.Create(
            action: "UserCreated",
            actorId: "system",
            resourceType: "User",
            resourceId: notification.Event.UserId.ToString(),
            payloadJson: $"{{\"email\":\"{notification.Event.Email}\"}}");

        await repository.AddAsync(entry, cancellationToken);
    }
}

/// <summary>
/// In-process bridge. RabbitMQ consumer can publish this notification after receiving the integration event.
/// </summary>
public sealed record UserCreatedNotification(UserCreatedIntegrationEvent Event) : INotification;
