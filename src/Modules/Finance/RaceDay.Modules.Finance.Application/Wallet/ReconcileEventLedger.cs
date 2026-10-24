using MediatR;
using Microsoft.Extensions.Logging;
using RaceDay.Modules.Event.Contracts.Finance;
using RaceDay.Modules.Finance.Application.Abstractions;
using RaceDay.Modules.Finance.Domain.Ledger;
using RaceDay.Modules.Payment.Contracts.Payments;

namespace RaceDay.Modules.Finance.Application.Wallet;

/// <summary>
/// Rebuild GrossRevenue / TicketsSold từ payment đã settle.
/// Gross = Paid + Refunded (doanh thu gộp lịch sử); TicketsSold = còn Paid (net).
/// RefundedTotal không đụng — DebitRefund đã cộng riêng.
/// </summary>
public static class EventLedgerReconciler
{
    public static async Task<EventLedger> ReconcileAsync(
        Guid eventId,
        EventFinanceInfo ev,
        IEventLedgerRepository ledgers,
        IEventFinanceQuery events,
        IPaymentFinancePort payments,
        IFinanceUnitOfWork uow,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var regIds = await events.ListPaidEligibleRegistrationIdsAsync(eventId, cancellationToken);
        decimal gross = 0m;
        var tickets = 0;
        foreach (var regId in regIds)
        {
            var settled = await payments.GetSettledByRegistrationAsync(regId, cancellationToken);
            if (settled is null)
            {
                continue;
            }

            // Gross giữ cả vé đã hoàn — UI: Doanh thu 399k + Hoàn 399k → khả dụng 0.
            gross += settled.Amount;
            if (string.Equals(settled.Status, "Paid", StringComparison.OrdinalIgnoreCase))
            {
                tickets++;
            }
        }

        var ledger = await ledgers.GetByEventIdAsync(eventId, cancellationToken);
        if (ledger is null)
        {
            ledger = EventLedger.Create(eventId, ev.OrganizerUserId, ev.Name, "VND");
            ledger.SyncTicketSalesFromPayments(tickets, gross);
            await ledgers.AddAsync(ledger, cancellationToken);
            logger.LogWarning(
                "Finance ledger missing for event {EventId} — created via reconcile ({Tickets} tickets, {Gross}).",
                eventId, tickets, gross);
        }
        else if (ledger.TicketsSold != tickets || ledger.GrossRevenue != gross)
        {
            logger.LogWarning(
                "Finance ledger drift for event {EventId}: DB {DbTickets}/{DbGross} → payments {Tickets}/{Gross}. Syncing.",
                eventId, ledger.TicketsSold, ledger.GrossRevenue, tickets, gross);
            ledger.Rename(ev.Name);
            ledger.SyncTicketSalesFromPayments(tickets, gross);
            ledgers.Update(ledger);
        }
        else
        {
            ledger.Rename(ev.Name);
            ledgers.Update(ledger);
        }

        await uow.SaveChangesAsync(cancellationToken);
        return ledger;
    }
}

public sealed record ReconcileEventLedgerCommand(Guid EventId) : IRequest<EventLedgerDto>;

public sealed class ReconcileEventLedgerCommandHandler(
    IEventLedgerRepository ledgers,
    IPlatformFinanceSettingsRepository settingsRepo,
    IEventFinanceQuery events,
    IPaymentFinancePort payments,
    IFinanceUnitOfWork uow,
    ILogger<ReconcileEventLedgerCommandHandler> logger,
    RaceDay.Shared.Application.Abstractions.ICurrentUserAccessor currentUser)
    : IRequestHandler<ReconcileEventLedgerCommand, EventLedgerDto>
{
    public async Task<EventLedgerDto> Handle(ReconcileEventLedgerCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId
            ?? throw new RaceDay.Shared.Domain.Exceptions.DomainException("Cần đăng nhập.");
        var ev = await events.GetEventAsync(request.EventId, cancellationToken)
            ?? throw new RaceDay.Shared.Domain.Exceptions.DomainException("Không tìm thấy giải.");
        if (!currentUser.IsAdmin && ev.OrganizerUserId != userId)
        {
            throw new RaceDay.Shared.Domain.Exceptions.DomainException("Bạn không phải chủ giải này.");
        }

        var ledger = await EventLedgerReconciler.ReconcileAsync(
            request.EventId, ev, ledgers, events, payments, uow, logger, cancellationToken);
        var settings = await settingsRepo.GetAsync(cancellationToken);
        return LedgerCalculator.ToDto(ledger, settings, ev);
    }
}
