using RaceDay.Modules.Event.Contracts.Finance;
using RaceDay.Modules.Finance.Domain.Ledger;
using RaceDay.Modules.Finance.Domain.Settings;
using RaceDay.Shared.Application.Time;

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

    /// <summary>Race start as UTC; EventDate/StartTime are Vietnam wall-clock values (midnight when no start time).</summary>
    private static DateTime StartUtc(EventFinanceInfo info)
        => RaceLocalTime.ToUtc(info.EventDate, info.StartTime ?? TimeOnly.MinValue);

    public static bool IsAfterOrAtEventStart(EventFinanceInfo? info)
    {
        if (info is null)
        {
            return false;
        }

        return DateTime.UtcNow >= StartUtc(info);
    }

    /// <summary>Số ngày còn lại đến EventDate (floor). Âm nếu đã qua ngày giải.</summary>
    public static int? DaysUntilEvent(EventFinanceInfo? info, DateTime? utcNow = null)
    {
        if (info is null)
        {
            return null;
        }

        // Whole Vietnam calendar days between today and the race day.
        var today = RaceLocalTime.Today(utcNow ?? DateTime.UtcNow);
        return info.EventDate.DayNumber - today.DayNumber;
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
            if (utcNow.Value >= StartUtc(eventInfo) && !settings.AllowRefundAfterEventStart)
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
