using MediatR;
using Microsoft.Extensions.Logging;
using RaceDay.Modules.Event.Application.Abstractions;
using RaceDay.Modules.Event.Domain.Events;
using RaceDay.Modules.Payment.Contracts.Events;
using RaceDay.Shared.Application.Abstractions;
using RaceDay.Shared.Application.Messaging;
using RaceDay.Shared.Domain.Exceptions;

namespace RaceDay.Modules.Event.Application.Registrations;

public sealed class PaymentCompletedEventHandler(
    IRaceEventRepository events,
    IEventRegistrationRepository registrations,
    IEventUnitOfWork unitOfWork,
    IEventBus eventBus,
    IEmailSender emailSender,
    ILogger<PaymentCompletedEventHandler> logger) : INotificationHandler<IntegrationEventOccurred>
{
    public async Task Handle(IntegrationEventOccurred notification, CancellationToken cancellationToken)
    {
        if (notification.Event is not PaymentCompletedIntegrationEvent paid)
        {
            return;
        }

        var reg = await registrations.GetByIdAsync(paid.RegistrationId, cancellationToken)
            ?? throw new DomainException($"Registration {paid.RegistrationId} not found for payment.");

        if (reg.Status != RegistrationStatus.PendingPayment)
        {
            return;
        }

        var race = await events.GetByIdWithDetailsAsync(reg.EventId, cancellationToken)
            ?? throw new DomainException("Không tìm thấy giải.");

        var distance = race.GetDistance(reg.DistanceId);
        var held = reg.HasSlotHold;

        // Hold còn → slot đã nằm trong RegisteredCount. Hold đã nhả (TTL) → reserve lại.
        if (!held)
        {
            if (!await events.TryReserveDistanceSlotAsync(reg.DistanceId, cancellationToken))
            {
                throw new DomainException("Cự ly đã hết slot sau khi thanh toán.");
            }

            race = await events.GetByIdWithDetailsAsync(reg.EventId, cancellationToken)
                ?? throw new DomainException("Không tìm thấy giải.");
            distance = race.GetDistance(reg.DistanceId);
        }

        reg.MarkPaidPendingApproval();
        registrations.Update(reg);

        // Hủy các đơn chờ TT khác cùng giải (tránh 1 user nhiều cự ly pending) + nhả hold.
        var siblings = await registrations.ListActiveByUserEventAsync(reg.EventId, reg.UserId, cancellationToken);
        foreach (var unpaid in siblings.Where(x =>
                     x.Id != reg.Id && x.Status == RegistrationStatus.PendingPayment))
        {
            try
            {
                var unpaidDistance = race.GetDistance(unpaid.DistanceId);
                RegistrationSlotHold.TryReleaseHeldSlot(unpaid, unpaidDistance);
            }
            catch (DomainException)
            {
                if (unpaid.HasSlotHold)
                {
                    await events.ReleaseDistanceSlotAsync(unpaid.DistanceId, cancellationToken);
                    unpaid.ClearSlotHold();
                }
            }

            unpaid.CancelUnpaid();
            registrations.Update(unpaid);
        }

        var autoApproved = await RegistrationAutoApproval.TryConfirmAsync(
            reg, race, distance, registrations, eventBus, emailSender, logger, cancellationToken);

        events.Update(race);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        if (autoApproved)
        {
            return;
        }

        try
        {
            await emailSender.SendAsync(
                RegistrationEmailTemplates.PaymentReceived(reg, race, distance, paid.Amount, paid.Currency),
                cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to send payment-received email for registration {RegistrationId}", reg.Id);
        }
    }
}
