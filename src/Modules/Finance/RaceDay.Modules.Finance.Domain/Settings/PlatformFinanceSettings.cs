using RaceDay.Shared.Domain.Abstractions;
using RaceDay.Shared.Domain.Exceptions;

namespace RaceDay.Modules.Finance.Domain.Settings;

/// <summary>
/// Platform finance rules.
/// Refund (theo ngày còn lại đến EventDate): ≥90 ngày → FullRefundPercent;
/// ≥60 ngày → PartialRefundPercent; &lt;60 ngày (gồm khóa ĐK T-4 tuần) → 0%.
/// Payout: tạm ứng PreEventWithdrawPercent khi còn ≤28 ngày trước giải;
/// phần còn lại (PostEventWithdrawPercent) sau khi giải đã qua giờ xuất phát / Completed.
/// </summary>
public sealed class PlatformFinanceSettings : AggregateRoot
{
    public static readonly Guid SingletonId = Guid.Parse("aaaaaaaa-bbbb-4ccc-8ddd-eeeeeeeeeeee");

    public const int FullRefundMinDaysBeforeEvent = 90;
    public const int PartialRefundMinDaysBeforeEvent = 60;
    public const int AdvancePayoutMaxDaysBeforeEvent = 28;

    public decimal PlatformFeePercent { get; private set; } = 5m;
    /// <summary>Đợt 1 — tạm ứng trước giải (mặc định 30% net).</summary>
    public decimal PreEventWithdrawPercent { get; private set; } = 30m;
    /// <summary>Đợt 2 — phần còn lại sau giải (mặc định 70% net).</summary>
    public decimal PostEventWithdrawPercent { get; private set; } = 70m;
    public decimal MinPayoutAmount { get; private set; } = 500_000m;
    /// <summary>Hoàn khi còn ≥ 90 ngày trước EventDate.</summary>
    public decimal FullRefundPercent { get; private set; } = 100m;
    /// <summary>Hoàn khi còn ≥ 60 và &lt; 90 ngày trước EventDate.</summary>
    public decimal PartialRefundPercent { get; private set; } = 85m;
    /// <summary>Nếu false, không hoàn runner khi đã tới/qua giờ xuất phát.</summary>
    public bool AllowRefundAfterEventStart { get; private set; }
    public string? Notes { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }

    private PlatformFinanceSettings()
    {
    }

    public static PlatformFinanceSettings CreateDefault()
    {
        return new PlatformFinanceSettings
        {
            Id = SingletonId,
            PlatformFeePercent = 5m,
            PreEventWithdrawPercent = 30m,
            PostEventWithdrawPercent = 70m,
            MinPayoutAmount = 500_000m,
            FullRefundPercent = 100m,
            PartialRefundPercent = 85m,
            AllowRefundAfterEventStart = false,
            Notes =
                "Phí 5%. Giải ngân: tạm ứng 30% từ T-4 tuần; 70% còn lại sau giải. " +
                "Yêu cầu hoàn VĐV: không hoàn lý do cá nhân; chỉ khiếu nại lỗi hệ thống (Manager/Admin duyệt → 100%). " +
                "BTC từ chối hồ sơ vẫn có thể hoàn theo mốc ngày (≥90/≥60).",
            UpdatedAtUtc = DateTime.UtcNow
        };
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

        if (preEventWithdrawPercent + postEventWithdrawPercent > 100.0001m)
        {
            throw new DomainException("Tổng % tạm ứng + sau giải không được vượt 100.");
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
