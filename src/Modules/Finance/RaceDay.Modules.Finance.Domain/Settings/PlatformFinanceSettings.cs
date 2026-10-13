using RaceDay.Shared.Domain.Abstractions;
using RaceDay.Shared.Domain.Exceptions;

namespace RaceDay.Modules.Finance.Domain.Settings;

/// <summary>
/// Platform-wide finance rules (Admin). Refund schedule defaults:
/// RegistrationOpen → FullRefundPercent; after close → PartialRefundPercent; at/after gun → 0%.
/// Organizer may store a per-event override that is not more generous than these ceilings.
/// </summary>
public sealed class PlatformFinanceSettings : AggregateRoot
{
    public static readonly Guid SingletonId = Guid.Parse("aaaaaaaa-bbbb-4ccc-8ddd-eeeeeeeeeeee");

    public decimal PlatformFeePercent { get; private set; } = 5m;
    public decimal PreEventWithdrawPercent { get; private set; } = 50m;
    public decimal PostEventWithdrawPercent { get; private set; } = 50m;
    public decimal MinPayoutAmount { get; private set; } = 500_000m;
    /// <summary>Refund % while event still RegistrationOpen.</summary>
    public decimal FullRefundPercent { get; private set; } = 100m;
    /// <summary>Refund % after registration closed until event start.</summary>
    public decimal PartialRefundPercent { get; private set; } = 50m;
    /// <summary>If false, no runner-initiated refund on/after event start.</summary>
    public bool AllowRefundAfterEventStart { get; private set; }
    public string? Notes { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }

    private PlatformFinanceSettings()
    {
    }

    public static PlatformFinanceSettings CreateDefault()
    {
        var s = new PlatformFinanceSettings
        {
            Id = SingletonId,
            PlatformFeePercent = 5m,
            PreEventWithdrawPercent = 50m,
            PostEventWithdrawPercent = 50m,
            MinPayoutAmount = 500_000m,
            FullRefundPercent = 100m,
            PartialRefundPercent = 50m,
            AllowRefundAfterEventStart = false,
            Notes = "Phí 5%. Rút tối đa 50% trước race day, phần còn lại sau giải. Hoàn 100% khi còn mở ĐK; 50% sau đóng ĐK; 0% từ giờ xuất phát.",
            UpdatedAtUtc = DateTime.UtcNow
        };
        return s;
    }

    public void Update(
        decimal platformFeePercent,
        decimal preEventWithdrawPercent,
        decimal postEventWithdrawPercent,
        decimal minPayoutAmount,
        decimal fullRefundPercent,
        decimal partialRefundPercent,
        bool allowRefundAfterEventStart,
        string? notes)
    {
        EnsurePercent(platformFeePercent, nameof(platformFeePercent));
        EnsurePercent(preEventWithdrawPercent, nameof(preEventWithdrawPercent));
        EnsurePercent(postEventWithdrawPercent, nameof(postEventWithdrawPercent));
        EnsurePercent(fullRefundPercent, nameof(fullRefundPercent));
        EnsurePercent(partialRefundPercent, nameof(partialRefundPercent));
        if (minPayoutAmount < 0)
        {
            throw new DomainException("minPayoutAmount không hợp lệ.");
        }

        PlatformFeePercent = platformFeePercent;
        PreEventWithdrawPercent = preEventWithdrawPercent;
        PostEventWithdrawPercent = postEventWithdrawPercent;
        MinPayoutAmount = minPayoutAmount;
        FullRefundPercent = fullRefundPercent;
        PartialRefundPercent = partialRefundPercent;
        AllowRefundAfterEventStart = allowRefundAfterEventStart;
        Notes = notes?.Trim();
        UpdatedAtUtc = DateTime.UtcNow;
    }

    private static void EnsurePercent(decimal value, string name)
    {
        if (value < 0 || value > 100)
        {
            throw new DomainException($"{name} phải trong 0–100.");
        }
    }
}
