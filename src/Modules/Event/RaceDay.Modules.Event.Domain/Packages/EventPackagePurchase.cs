using RaceDay.Shared.Domain.Abstractions;
using RaceDay.Shared.Domain.Exceptions;

namespace RaceDay.Modules.Event.Domain.Packages;

public enum PackagePurchaseStatus
{
    PendingPayment = 0,
    Paid = 1,
    Cancelled = 2,
    Expired = 3
}

public sealed class EventPackagePurchase : AggregateRoot
{
    public Guid OrganizerUserId { get; private set; }
    public string OrgName { get; private set; } = string.Empty;
    public string? OrgIntro { get; private set; }
    public string? OrgAddress { get; private set; }
    public string? InvoiceInfo { get; private set; }
    public int DurationMonths { get; private set; }
    public int ParticipantCap { get; private set; }
    public string? PromoCode { get; private set; }
    public decimal BasePrice { get; private set; }
    public decimal CapacityFee { get; private set; }
    public decimal Discount { get; private set; }
    public decimal TotalAmount { get; private set; }
    public string Currency { get; private set; } = "VND";
    public PackagePurchaseStatus Status { get; private set; } = PackagePurchaseStatus.PendingPayment;
    public Guid? PaymentId { get; private set; }
    public Guid? CreatedEventId { get; private set; }
    public DateTime? PackageExpiresAtUtc { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }

    private EventPackagePurchase()
    {
    }

    public static EventPackagePurchase Create(
        Guid organizerUserId,
        string orgName,
        string? orgIntro,
        string? orgAddress,
        string? invoiceInfo,
        int durationMonths,
        int participantCap,
        string? promoCode,
        decimal basePrice,
        decimal capacityFee,
        decimal discount,
        decimal totalAmount,
        string currency)
    {
        if (organizerUserId == Guid.Empty)
        {
            throw new DomainException("OrganizerUserId không hợp lệ.");
        }

        if (string.IsNullOrWhiteSpace(orgName))
        {
            throw new DomainException("Tên ban tổ chức không được để trống.");
        }

        if (totalAmount <= 0)
        {
            throw new DomainException("Tổng tiền phải > 0.");
        }

        var now = DateTime.UtcNow;
        return new EventPackagePurchase
        {
            OrganizerUserId = organizerUserId,
            OrgName = orgName.Trim(),
            OrgIntro = orgIntro?.Trim(),
            OrgAddress = orgAddress?.Trim(),
            InvoiceInfo = invoiceInfo?.Trim(),
            DurationMonths = durationMonths,
            ParticipantCap = participantCap,
            PromoCode = promoCode?.Trim(),
            BasePrice = basePrice,
            CapacityFee = capacityFee,
            Discount = discount,
            TotalAmount = totalAmount,
            Currency = string.IsNullOrWhiteSpace(currency) ? "VND" : currency.Trim().ToUpperInvariant(),
            Status = PackagePurchaseStatus.PendingPayment,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };
    }

    public void MarkPaid(Guid paymentId)
    {
        if (Status == PackagePurchaseStatus.Paid)
        {
            return;
        }

        if (Status != PackagePurchaseStatus.PendingPayment)
        {
            throw new DomainException($"Không thể mark paid từ trạng thái {Status}.");
        }

        Status = PackagePurchaseStatus.Paid;
        PaymentId = paymentId;
        Touch();
    }

    public void SetCreatedEvent(Guid eventId, DateTime expiresAtUtc)
    {
        CreatedEventId = eventId;
        PackageExpiresAtUtc = expiresAtUtc;
        Touch();
    }

    public void MarkCancelled()
    {
        if (Status == PackagePurchaseStatus.Paid)
        {
            throw new DomainException("Không hủy gói đã thanh toán.");
        }

        Status = PackagePurchaseStatus.Cancelled;
        Touch();
    }

    private void Touch() => UpdatedAtUtc = DateTime.UtcNow;
}
