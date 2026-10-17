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
        if (!distance.HasAvailableSlot)
        {
            throw new DomainException("Cự ly đã hết slot sau khi thanh toán.");
        }

        reg.MarkPaidPendingApproval();
        distance.IncrementRegistered();
        registrations.Update(reg);

        // Hủy các đơn chờ TT khác cùng giải (tránh 1 user nhiều cự ly pending).
        var siblings = await registrations.ListActiveByUserEventAsync(reg.EventId, reg.UserId, cancellationToken);
        foreach (var unpaid in siblings.Where(x =>
                     x.Id != reg.Id && x.Status == RegistrationStatus.PendingPayment))
        {
            unpaid.CancelUnpaid();
            registrations.Update(unpaid);
        }

        events.Update(race);
        await unitOfWork.SaveChangesAsync(cancellationToken);

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
