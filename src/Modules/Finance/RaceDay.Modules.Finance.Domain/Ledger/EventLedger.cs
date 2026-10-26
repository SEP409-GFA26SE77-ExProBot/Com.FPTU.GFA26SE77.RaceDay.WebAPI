using RaceDay.Shared.Domain.Abstractions;
using RaceDay.Shared.Domain.Exceptions;

namespace RaceDay.Modules.Finance.Domain.Ledger;

public sealed class EventLedger : AggregateRoot
{
    public Guid EventId { get; private set; }
    public Guid OrganizerUserId { get; private set; }
    public string EventName { get; private set; } = string.Empty;
    public string Currency { get; private set; } = "VND";
    public int TicketsSold { get; private set; }
    public decimal GrossRevenue { get; private set; }
    public decimal RefundedTotal { get; private set; }
    public decimal WithdrawnTotal { get; private set; }
    public decimal PayoutPending { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }

    private EventLedger()
    {
    }

    public static EventLedger Create(Guid eventId, Guid organizerUserId, string eventName, string currency)
    {
        if (eventId == Guid.Empty || organizerUserId == Guid.Empty)
        {
            throw new DomainException("Event/Organizer không hợp lệ.");
        }

        return new EventLedger
        {
            EventId = eventId,
            OrganizerUserId = organizerUserId,
            EventName = string.IsNullOrWhiteSpace(eventName) ? "Giải" : eventName.Trim(),
            Currency = string.IsNullOrWhiteSpace(currency) ? "VND" : currency.Trim().ToUpperInvariant(),
            UpdatedAtUtc = DateTime.UtcNow
        };
    }

    public void Rename(string eventName)
    {
        if (!string.IsNullOrWhiteSpace(eventName))
        {
            EventName = eventName.Trim();
            UpdatedAtUtc = DateTime.UtcNow;
        }
    }

    public void CreditTicketSale(decimal amount)
    {
        if (amount <= 0)
        {
            throw new DomainException("Số tiền vé không hợp lệ.");
        }

        GrossRevenue += amount;
        TicketsSold++;
        UpdatedAtUtc = DateTime.UtcNow;
    }

    /// <summary>
    /// Đồng bộ lại: gross = tổng đã thu (Paid+Refunded); ticketsSold = vé còn Paid.
    /// Không đụng RefundedTotal / Withdrawn / Pending.
    /// </summary>
    public void SyncTicketSalesFromPayments(int ticketsSold, decimal grossRevenue)
    {
        if (ticketsSold < 0 || grossRevenue < 0)
        {
            throw new DomainException("Snapshot doanh thu không hợp lệ.");
        }

        TicketsSold = ticketsSold;
        GrossRevenue = grossRevenue;
        UpdatedAtUtc = DateTime.UtcNow;
    }

    public void DebitRefund(decimal amount)
    {
        if (amount <= 0)
        {
            throw new DomainException("Số tiền hoàn không hợp lệ.");
        }

        RefundedTotal += amount;
        if (TicketsSold > 0)
        {
            TicketsSold--;
        }

        UpdatedAtUtc = DateTime.UtcNow;
    }

    public void ReservePayout(decimal amount)
    {
        if (amount <= 0)
        {
            throw new DomainException("Số tiền rút không hợp lệ.");
        }

        PayoutPending += amount;
        UpdatedAtUtc = DateTime.UtcNow;
    }

    public void CompletePayout(decimal amount)
    {
        if (amount <= 0)
        {
            throw new DomainException("Số tiền rút không hợp lệ.");
        }

        if (PayoutPending < amount)
        {
            throw new DomainException("Payout pending không đủ.");
        }

        PayoutPending -= amount;
        WithdrawnTotal += amount;
        UpdatedAtUtc = DateTime.UtcNow;
    }

    public void ReleasePayoutReserve(decimal amount)
    {
        if (amount <= 0)
        {
            return;
        }

        PayoutPending = Math.Max(0, PayoutPending - amount);
        UpdatedAtUtc = DateTime.UtcNow;
    }

    public decimal PlatformFeeAmount(decimal feePercent)
        => Math.Round(GrossRevenue * feePercent / 100m, 0, MidpointRounding.AwayFromZero);

    public decimal NetToOrganizer(decimal feePercent)
        => Math.Max(0, GrossRevenue - PlatformFeeAmount(feePercent) - RefundedTotal);

    public decimal EscrowHeld(decimal feePercent)
        => Math.Max(0, NetToOrganizer(feePercent) - WithdrawnTotal - PayoutPending);
}
