using System.Text.Json;
using MediatR;
using Microsoft.Extensions.Logging;
using RaceDay.Modules.AuditLog.Application.Abstractions;
using RaceDay.Modules.AuditLog.Domain.AuditEntries;
using RaceDay.Modules.Iam.Contracts.Events;
using RaceDay.Shared.Application.Messaging;

namespace RaceDay.Modules.AuditLog.Application.EventHandlers;

public sealed class IamAuditEventHandler(
    IAuditEntryRepository repository,
    ILogger<IamAuditEventHandler> logger)
    : INotificationHandler<IntegrationEventOccurred>
{
    public async Task Handle(IntegrationEventOccurred notification, CancellationToken cancellationToken)
    {
        var entry = notification.Event switch
        {
            UserCreatedIntegrationEvent e => AuditEntry.Create(
                "UserCreated", "system", "User", e.UserId.ToString(),
                JsonSerializer.Serialize(new { e.Email, e.FullName })),
            UserUpdatedIntegrationEvent e => AuditEntry.Create(
                "UserUpdated", "system", "User", e.UserId.ToString(),
                JsonSerializer.Serialize(new { e.Email, e.FullName })),
            UserDeletedIntegrationEvent e => AuditEntry.Create(
                "UserDeleted", "system", "User", e.UserId.ToString(),
                JsonSerializer.Serialize(new { e.Email })),
            UserLoggedInIntegrationEvent e => AuditEntry.Create(
                "UserLoggedIn", e.UserId.ToString(), "User", e.UserId.ToString(),
                JsonSerializer.Serialize(new { e.Email })),
            UserLockedIntegrationEvent e => AuditEntry.Create(
                "UserLocked", "system", "User", e.UserId.ToString(),
                JsonSerializer.Serialize(new { e.Email })),
            UserUnlockedIntegrationEvent e => AuditEntry.Create(
                "UserUnlocked", "system", "User", e.UserId.ToString(),
                JsonSerializer.Serialize(new { e.Email })),
            UserEmailVerifiedIntegrationEvent e => AuditEntry.Create(
                "UserEmailVerified", e.UserId.ToString(), "User", e.UserId.ToString(),
                JsonSerializer.Serialize(new { e.Email })),
            PasswordResetIntegrationEvent e => AuditEntry.Create(
                "PasswordReset", e.UserId.ToString(), "User", e.UserId.ToString(),
                JsonSerializer.Serialize(new { e.Email })),
            _ => null
        };

        if (entry is null)
        {
            return;
        }

        try
        {
            await repository.AddAsync(entry, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to write audit entry for {Action}", entry.Action);
        }
    }
}
