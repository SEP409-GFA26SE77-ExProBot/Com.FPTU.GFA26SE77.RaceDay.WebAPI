using MediatR;
using Microsoft.Extensions.Logging;
using RaceDay.Modules.Event.Contracts.Finance;
using RaceDay.Modules.Finance.Application.Abstractions;
using RaceDay.Modules.Finance.Domain.Ledger;
using RaceDay.Modules.Payment.Contracts.Events;
using RaceDay.Shared.Application.Messaging;

namespace RaceDay.Modules.Finance.Application.Wallet;

public sealed class PaymentCompletedLedgerHandler(
    IEventLedgerRepository ledgers,
    IEventFinanceQuery events,
    IFinanceUnitOfWork uow,
    ILogger<PaymentCompletedLedgerHandler> logger) : INotificationHandler<IntegrationEventOccurred>
{
    public async Task Handle(IntegrationEventOccurred notification, CancellationToken cancellationToken)
    {
        if (notification.Event is not PaymentCompletedIntegrationEvent paid)
        {
            return;
        }

        try
        {
            var reg = await events.GetRegistrationAsync(paid.RegistrationId, cancellationToken);
            if (reg is null)
            {
                logger.LogError(
                    "PaymentCompleted ledger skip: registration {RegistrationId} not found (payment {PaymentId}).",
                    paid.RegistrationId, paid.PaymentId);
                return;
            }

            var ledger = await ledgers.GetByEventIdAsync(reg.EventId, cancellationToken);
            if (ledger is null)
            {
                ledger = EventLedger.Create(reg.EventId, reg.OrganizerUserId, reg.EventName, paid.Currency);
                await ledgers.AddAsync(ledger, cancellationToken);
            }
            else
            {
                ledger.Rename(reg.EventName);
            }

            ledger.CreditTicketSale(paid.Amount);
            ledgers.Update(ledger);
            await uow.SaveChangesAsync(cancellationToken);

            logger.LogInformation(
                "Ledger credited {Amount} for event {EventId} from payment {PaymentId} (tickets={Tickets}, gross={Gross}).",
                paid.Amount, reg.EventId, paid.PaymentId, ledger.TicketsSold, ledger.GrossRevenue);
        }
        catch (Exception ex)
        {
            // Không nuốt im lặng — log rõ để biết vì sao ví vẫn 0đ.
            logger.LogError(
                ex,
                "Failed to credit finance ledger for payment {PaymentId} / registration {RegistrationId}.",
                paid.PaymentId, paid.RegistrationId);
            throw;
        }
    }
}
