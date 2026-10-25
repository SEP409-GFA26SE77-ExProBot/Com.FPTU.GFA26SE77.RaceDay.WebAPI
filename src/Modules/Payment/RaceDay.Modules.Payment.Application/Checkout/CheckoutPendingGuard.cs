using Microsoft.Extensions.Configuration;
using RaceDay.Modules.Payment.Domain;
using RaceDay.Shared.Domain.Exceptions;

namespace RaceDay.Modules.Payment.Application;

/// <summary>
/// Idempotency + PayOS link TTL:
/// - Reuse một pending checkout còn sống (cùng registration / package).
/// - Link hết hạn (PayOS EXPIRED/CANCELLED hoặc quá LinkTtlMinutes) → MarkExpired, cho phép tạo order mới.
/// </summary>
public static class CheckoutPendingGuard
{
    public const int DefaultLinkTtlMinutes = 15;

    public enum PendingOutcome
    {
        /// <summary>Tái dùng pending + CheckoutUrl hiện tại.</summary>
        Reuse,

        /// <summary>Pending nhưng chưa có URL — tiếp tục AttachCheckout trên cùng order.</summary>
        ContinueCreate,

        /// <summary>Đã hết hạn / hủy — caller tạo PaymentOrder mới.</summary>
        Recreate,

        /// <summary>PayOS báo PAID — caller ConfirmPaymentPaid.</summary>
        AlreadyPaid
    }

    public static int ResolveTtlMinutes(IConfiguration configuration)
    {
        var raw = configuration["PayOS:LinkTtlMinutes"];
        if (int.TryParse(raw, out var m) && m is >= 1 and <= 120)
        {
            return m;
        }

        return DefaultLinkTtlMinutes;
    }

    public static bool IsTerminalPayOsStatus(string? status)
    {
        if (string.IsNullOrWhiteSpace(status))
        {
            return false;
        }

        return status.Equals("EXPIRED", StringComparison.OrdinalIgnoreCase)
               || status.Equals("CANCELLED", StringComparison.OrdinalIgnoreCase)
               || status.Equals("CANCELED", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsPaidPayOsStatus(string? status)
        => string.Equals(status, "PAID", StringComparison.OrdinalIgnoreCase);

    public static bool IsLocallyExpired(PaymentOrder payment, int ttlMinutes)
    {
        if (payment.Status != PaymentStatus.Pending)
        {
            return payment.Status is PaymentStatus.Expired or PaymentStatus.Cancelled or PaymentStatus.Failed;
        }

        var anchor = payment.UpdatedAtUtc == default ? payment.CreatedAtUtc : payment.UpdatedAtUtc;
        return DateTime.UtcNow - anchor > TimeSpan.FromMinutes(ttlMinutes);
    }

    /// <summary>
    /// Quyết định tái dùng / tạo lại pending checkout. Có thể MarkExpired trên <paramref name="payment"/>.
    /// </summary>
    public static async Task<PendingOutcome> EvaluateAsync(
        PaymentOrder payment,
        IPayOsGateway payOs,
        int ttlMinutes,
        CancellationToken cancellationToken)
    {
        if (payment.Status == PaymentStatus.Paid)
        {
            return PendingOutcome.AlreadyPaid;
        }

        if (payment.Status is PaymentStatus.Expired or PaymentStatus.Cancelled or PaymentStatus.Failed or PaymentStatus.Refunded)
        {
            return PendingOutcome.Recreate;
        }

        // Pending chưa gắn URL → tạo link trên cùng order (idempotent retry sau lỗi mạng).
        if (string.IsNullOrWhiteSpace(payment.CheckoutUrl))
        {
            return PendingOutcome.ContinueCreate;
        }

        if (payOs.IsMock)
        {
            if (IsLocallyExpired(payment, ttlMinutes))
            {
                payment.MarkExpired();
                return PendingOutcome.Recreate;
            }

            return PendingOutcome.Reuse;
        }

        try
        {
            var info = await payOs.GetPaymentLinkAsync(payment.OrderCode, cancellationToken);
            if (info is not null)
            {
                if (IsPaidPayOsStatus(info.Status))
                {
                    return PendingOutcome.AlreadyPaid;
                }

                if (IsTerminalPayOsStatus(info.Status))
                {
                    payment.MarkExpired();
                    return PendingOutcome.Recreate;
                }

                // PENDING / PROCESSING …
                if (IsLocallyExpired(payment, ttlMinutes))
                {
                    // PayOS vẫn PENDING nhưng quá TTL nội bộ — không tái dùng QR cũ.
                    payment.MarkExpired();
                    return PendingOutcome.Recreate;
                }

                return PendingOutcome.Reuse;
            }
        }
        catch (DomainException)
        {
            throw;
        }
        catch
        {
            // PayOS lỗi tạm — fallback TTL local.
        }

        if (IsLocallyExpired(payment, ttlMinutes))
        {
            payment.MarkExpired();
            return PendingOutcome.Recreate;
        }

        return PendingOutcome.Reuse;
    }
}
