using RaceDay.Modules.Event.Contracts.Finance;
using RaceDay.Modules.Finance.Domain.Ledger;
using RaceDay.Modules.Finance.Domain.Settings;

namespace RaceDay.Modules.Finance.Application.Wallet;

public static class LedgerCalculator
{
    public const string PhaseTooEarly = "too_early";
    public const string PhaseAdvance = "advance";
    public const string PhaseFinal = "final";

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
        var daysUntil = DaysUntilEvent(eventInfo);
        var advanceCap = Math.Round(
            net * settings.PreEventWithdrawPercent / 100m,
            0,
            MidpointRounding.AwayFromZero);
        var finalCap = net;

        string phase;
        decimal withdrawCap;
        string note;
        if (afterEvent || status is "Completed")
        {
            phase = PhaseFinal;
            withdrawCap = finalCap;
            note =
                $"Đợt 2 — sau giải: rút phần escrow còn lại (mục tiêu ~{settings.PostEventWithdrawPercent:0.#}% net; đã tạm ứng + phí + hoàn đã trừ).";
        }
        else if (daysUntil is not null
                 && daysUntil.Value <= PlatformFinanceSettings.AdvancePayoutMaxDaysBeforeEvent
                 && daysUntil.Value >= 0)
        {
            phase = PhaseAdvance;
            withdrawCap = advanceCap;
            note =
                $"Đợt 1 — tạm ứng T-4 tuần: tối đa {settings.PreEventWithdrawPercent:0.#}% net sau phí & hoàn " +
                $"(còn {daysUntil.Value} ngày đến giải).";
        }
        else
        {
            phase = PhaseTooEarly;
            withdrawCap = 0;
            note = daysUntil is null
                ? "Chưa đủ thông tin ngày giải — chưa mở tạm ứng."
                : $"Chưa tới mốc tạm ứng T-4 tuần (còn {daysUntil.Value} ngày; mở khi ≤ {PlatformFinanceSettings.AdvancePayoutMaxDaysBeforeEvent} ngày).";
        }

        var alreadyOut = ledger.WithdrawnTotal + ledger.PayoutPending;
        var available = Math.Max(0, Math.Min(escrow, withdrawCap - alreadyOut));

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
            ledger.Currency,
            phase,
            advanceCap,
            finalCap,
            daysUntil);
    }

    public static bool IsAfterOrAtEventStart(EventFinanceInfo? info)
    {
        if (info is null)
        {
            return false;
        }

        var startLocal = info.EventDate.ToDateTime(info.StartTime ?? TimeOnly.MinValue);
        var startUtc = DateTime.SpecifyKind(startLocal, DateTimeKind.Utc);
        return DateTime.UtcNow >= startUtc;
    }

    /// <summary>Số ngày còn lại đến EventDate (floor). Âm nếu đã qua ngày giải.</summary>
    public static int? DaysUntilEvent(EventFinanceInfo? info, DateTime? utcNow = null)
    {
        if (info is null)
        {
            return null;
        }

        var now = utcNow ?? DateTime.UtcNow;
        var startLocal = info.EventDate.ToDateTime(info.StartTime ?? TimeOnly.MinValue);
        var startUtc = DateTime.SpecifyKind(startLocal, DateTimeKind.Utc);
        return (int)Math.Floor((startUtc.Date - now.Date).TotalDays);
    }

    /// <summary>
    /// Hoàn theo mốc ngày còn lại đến giải (không phụ thuộc RegistrationOpen):
    /// ≥90 ngày → Full; ≥60 ngày → Partial; &lt;60 hoặc đã xuất phát → 0.
    /// </summary>
    public static (decimal Percent, decimal Amount) ComputeRefund(
        decimal paidAmount,
        PlatformFinanceSettings settings,
        EventFinanceInfo eventInfo,
        DateTime? utcNow = null)
    {
        if (IsAfterOrAtEventStart(eventInfo) && utcNow is null)
        {
            return settings.AllowRefundAfterEventStart ? (0, 0) : (0, 0);
        }

        if (utcNow is not null)
        {
            var startLocal = eventInfo.EventDate.ToDateTime(eventInfo.StartTime ?? TimeOnly.MinValue);
            var startUtc = DateTime.SpecifyKind(startLocal, DateTimeKind.Utc);
            if (utcNow.Value >= startUtc && !settings.AllowRefundAfterEventStart)
            {
                return (0, 0);
            }
        }
        else if (IsAfterOrAtEventStart(eventInfo))
        {
            return (0, 0);
        }

        var days = DaysUntilEvent(eventInfo, utcNow) ?? 0;
        decimal percent;
        if (days >= PlatformFinanceSettings.FullRefundMinDaysBeforeEvent)
        {
            percent = settings.FullRefundPercent;
        }
        else if (days >= PlatformFinanceSettings.PartialRefundMinDaysBeforeEvent)
        {
            percent = settings.PartialRefundPercent;
        }
        else
        {
            percent = 0;
        }

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
