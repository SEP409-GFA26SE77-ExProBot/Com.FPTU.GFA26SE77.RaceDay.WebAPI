using System.Text.Json;
using MediatR;
using Microsoft.Extensions.Logging;
using RaceDay.Modules.AuditLog.Application.Abstractions;
using RaceDay.Modules.AuditLog.Domain.AuditEntries;
using RaceDay.Modules.Event.Contracts.Events;
using RaceDay.Modules.Finance.Contracts.Events;
using RaceDay.Modules.Iam.Contracts.Events;
using RaceDay.Modules.Payment.Contracts.Events;
using RaceDay.Shared.Application.Messaging;

namespace RaceDay.Modules.AuditLog.Application.EventHandlers;

/// <summary>
/// Ghi audit từ integration events (IAM + Event + Payment + Finance).
/// Append-only Mongo — không UoW.
/// </summary>
public sealed class IamAuditEventHandler(
    IAuditEntryRepository repository,
    ILogger<IamAuditEventHandler> logger)
    : INotificationHandler<IntegrationEventOccurred>
{
    public async Task Handle(IntegrationEventOccurred notification, CancellationToken cancellationToken)
    {
        var entry = notification.Event switch
        {
            // ── IAM ──────────────────────────────────────────────────
            UserCreatedIntegrationEvent e => AuditEntry.Create(
                "UserCreated", "system", "User", e.UserId.ToString(),
                JsonSerializer.Serialize(new { e.Email, e.Username, e.FullName })),
            UserUpdatedIntegrationEvent e => AuditEntry.Create(
                "UserUpdated", "system", "User", e.UserId.ToString(),
                JsonSerializer.Serialize(new { e.Email, e.Username, e.FullName })),
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
            OrganizerRoleRequiredIntegrationEvent e => AuditEntry.Create(
                "OrganizerRoleAssigned", "system", "User", e.UserId.ToString(),
                JsonSerializer.Serialize(new { e.UserId, role = "Organizer" })),

            // ── Event ────────────────────────────────────────────────
            RaceEventCreatedIntegrationEvent e => AuditEntry.Create(
                "RaceEventCreated", e.OrganizerUserId.ToString(), "RaceEvent", e.RaceEventId.ToString(),
                JsonSerializer.Serialize(new { e.Name, e.OrganizerUserId })),
            RaceEventSubmittedIntegrationEvent e => AuditEntry.Create(
                "RaceEventSubmitted", e.OrganizerUserId.ToString(), "RaceEvent", e.RaceEventId.ToString(),
                JsonSerializer.Serialize(new { e.Name, e.OrganizerUserId })),
            RaceEventApprovedIntegrationEvent e => AuditEntry.Create(
                "RaceEventApproved", "admin", "RaceEvent", e.RaceEventId.ToString(),
                JsonSerializer.Serialize(new { e.Name, e.OrganizerUserId })),
            RaceEventRejectedIntegrationEvent e => AuditEntry.Create(
                "RaceEventRejected", "admin", "RaceEvent", e.RaceEventId.ToString(),
                JsonSerializer.Serialize(new { e.Name, e.OrganizerUserId, e.Reason })),
            RegistrationCreatedIntegrationEvent e => AuditEntry.Create(
                "RegistrationCreated", e.UserId.ToString(), "Registration", e.RegistrationId.ToString(),
                JsonSerializer.Serialize(new { e.RaceEventId, e.UserId })),
            RegistrationConfirmedIntegrationEvent e => AuditEntry.Create(
                "RegistrationConfirmed", e.UserId.ToString(), "Registration", e.RegistrationId.ToString(),
                JsonSerializer.Serialize(new { e.RaceEventId, e.BibNumber, e.TicketCode })),
            RegistrationRejectedIntegrationEvent e => AuditEntry.Create(
                "RegistrationRejected", "system", "Registration", e.RegistrationId.ToString(),
                JsonSerializer.Serialize(new { e.RaceEventId, e.UserId, e.Reason })),

            // ── Payment ──────────────────────────────────────────────
            PaymentCompletedIntegrationEvent e => AuditEntry.Create(
                "PaymentCompleted", e.UserId.ToString(), "Payment", e.PaymentId.ToString(),
                JsonSerializer.Serialize(new { e.RegistrationId, e.Amount, e.Currency, e.Gateway, e.TransactionReference })),
            PackagePaymentCompletedIntegrationEvent e => AuditEntry.Create(
                "PackagePaymentCompleted", e.UserId.ToString(), "Payment", e.PaymentId.ToString(),
                JsonSerializer.Serialize(new { e.PackagePurchaseId, e.Amount, e.Currency, e.Gateway, e.TransactionReference })),
            PaymentFailedIntegrationEvent e => AuditEntry.Create(
                "PaymentFailed", "system", "Payment", e.PaymentId.ToString(),
                JsonSerializer.Serialize(new { e.RegistrationId, e.Reason })),

            // ── Finance ──────────────────────────────────────────────
            RefundCompletedIntegrationEvent e => AuditEntry.Create(
                "RefundCompleted", e.UserId.ToString(), "Refund", e.RefundRequestId.ToString(),
                JsonSerializer.Serialize(new { e.PaymentId, e.RegistrationId, e.RaceEventId, e.RefundAmount })),

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
