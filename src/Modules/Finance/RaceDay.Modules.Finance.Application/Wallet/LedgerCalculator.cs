using RaceDay.Modules.Event.Contracts.Finance;
using RaceDay.Modules.Finance.Domain.Ledger;
using RaceDay.Modules.Finance.Domain.Settings;

namespace RaceDay.Modules.Finance.Application.Wallet;

public static class LedgerCalculator
{
    public static EventLedgerDto ToDto(
        EventLedger ledger,
        PlatformFinanceSettings settings,
        EventFinanceInfo? eventInfo)
    {
        var feePercent = settings.PlatformFeePercent;
        var fee = ledger.PlatformFeeAmount(feePercent);
        var net = ledger.NetToOrganizer(feePercent);
        var escrow = ledger.EscrowHeld(feePercent);
        var status = eventInfo?.Status ?? "Unknown";
        var afterEvent = IsAfterOrAtEventStart(eventInfo);
        var completed = status is "Completed" or "Closed" || afterEvent;

        decimal withdrawCap;
        string note;
        if (completed)
        {
            // Remaining slice after pre-event cap (postEventWithdrawPercent of net), plus unused pre-cap.
            withdrawCap = net;
            note = "Sau race day — có thể rút phần escrow còn lại (trừ phí & hoàn tiền).";
        }
        else
        {
            withdrawCap = Math.Round(net * settings.PreEventWithdrawPercent / 100m, 0, MidpointRounding.AwayFromZero);
            note = $"Trước sự kiện — tối đa {settings.PreEventWithdrawPercent:0.#}% net sau phí & hoàn tiền.";
        }

        var available = Math.Max(0, Math.Min(escrow, withdrawCap - ledger.WithdrawnTotal - ledger.PayoutPending));

        return new EventLedgerDto(
            ledger.EventId,
            eventInfo?.Name ?? ledger.EventName,
            ToFeStatus(status),
            ledger.TicketsSold,
            ledger.GrossRevenue,
            feePercent,
            fee,
            ledger.RefundedTotal,
            net,
            escrow,
            ledger.WithdrawnTotal,
            ledger.PayoutPending,
            available,
            note,
            ledger.Currency);
    }

    public static bool IsAfterOrAtEventStart(EventFinanceInfo? info)
    {
        if (info is null)
        {
            return false;
        }

        var startLocal = info.EventDate.ToDateTime(info.StartTime ?? TimeOnly.MinValue);
        // EventDate/StartTime stored as local VN wall-clock without offset — treat as UTC for policy cut.
        var startUtc = DateTime.SpecifyKind(startLocal, DateTimeKind.Utc);
        return DateTime.UtcNow >= startUtc;
    }

    public static (decimal Percent, decimal Amount) ComputeRefund(
        decimal paidAmount,
        PlatformFinanceSettings settings,
        EventFinanceInfo eventInfo)
    {
        if (IsAfterOrAtEventStart(eventInfo))
        {
            if (!settings.AllowRefundAfterEventStart)
            {
                return (0, 0);
            }

            return (0, 0);
        }

        var open = eventInfo.Status is "RegistrationOpen";
        var percent = open ? settings.FullRefundPercent : settings.PartialRefundPercent;
        var amount = Math.Round(paidAmount * percent / 100m, 0, MidpointRounding.AwayFromZero);
        return (percent, amount);
    }

    private static string ToFeStatus(string status) => status switch
    {
        "Draft" => "draft",
        "PendingApproval" => "pending-approval",
        "Approved" => "approved",
        "Rejected" => "rejected",
        "RegistrationOpen" => "registration-open",
        "Closed" => "closed",
        "Completed" => "completed",
        "Cancelled" => "cancelled",
        _ => status.ToLowerInvariant()
    };
}
