using MediatR;
using RaceDay.Modules.Event.Contracts.Finance;
using RaceDay.Modules.Finance.Application.Abstractions;
using RaceDay.Modules.Finance.Domain.Ledger;
using RaceDay.Modules.Payment.Contracts.Events;
using RaceDay.Shared.Application.Messaging;

namespace RaceDay.Modules.Finance.Application.Wallet;

public sealed class PaymentCompletedLedgerHandler(
    IEventLedgerRepository ledgers,
    IEventFinanceQuery events,
    IFinanceUnitOfWork uow) : INotificationHandler<IntegrationEventOccurred>
{
    public async Task Handle(IntegrationEventOccurred notification, CancellationToken cancellationToken)
    {
        if (notification.Event is not PaymentCompletedIntegrationEvent paid)
        {
            return;
        }

        var reg = await events.GetRegistrationAsync(paid.RegistrationId, cancellationToken);
        if (reg is null)
        {
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
    }
}
