using Microsoft.Extensions.Configuration;
using RaceDay.Modules.Event.Domain.Events;
using RaceDay.Modules.Event.Domain.Registrations;

namespace RaceDay.Modules.Event.Application.Registrations;

/// <summary>TTL giữ chỗ PendingPayment — khớp cửa sổ PayOS khi có thể.</summary>
public static class RegistrationSlotHold
{
    public const int DefaultHoldMinutes = 15;

    public static int ResolveHoldMinutes(IConfiguration configuration)
    {
        var raw = configuration["Registration:SlotHoldMinutes"]
                  ?? configuration["PayOS:LinkTtlMinutes"];
        if (int.TryParse(raw, out var m) && m is >= 1 and <= 120)
        {
            return m;
        }

        return DefaultHoldMinutes;
    }

    public static DateTime HoldUntilUtc(IConfiguration configuration, DateTime? utcNow = null)
        => (utcNow ?? DateTime.UtcNow).AddMinutes(ResolveHoldMinutes(configuration));

    /// <summary>
    /// Nhả hold trên domain distance đang track (cùng UoW). Caller phải CancelUnpaid / Clear riêng nếu cần.
    /// </summary>
    public static bool TryReleaseHeldSlot(EventRegistration reg, EventDistance distance)
    {
        if (!reg.HasSlotHold)
        {
            return false;
        }

        distance.DecrementRegistered();
        reg.ClearSlotHold();
        return true;
    }
}
