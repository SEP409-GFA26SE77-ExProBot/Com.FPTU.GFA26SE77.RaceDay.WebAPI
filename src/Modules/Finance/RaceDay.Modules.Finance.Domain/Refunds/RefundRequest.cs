using RaceDay.Shared.Domain.Abstractions;
using RaceDay.Shared.Domain.Exceptions;

namespace RaceDay.Modules.Finance.Domain.Refunds;

public enum RefundRequestStatus
{
    Requested = 0,
    Processing = 1,
    Refunded = 2,
    Rejected = 3
}

public sealed class RefundRequest : AggregateRoot
{
    public Guid PaymentId { get; private set; }
    public Guid RegistrationId { get; private set; }
    public Guid EventId { get; private set; }
    public Guid UserId { get; private set; }
    public decimal PaidAmount { get; private set; }
    public decimal RefundAmount { get; private set; }
    public decimal RefundPercent { get; private set; }
    public string Reason { get; private set; } = string.Empty;
    public RefundRequestStatus Status { get; private set; } = RefundRequestStatus.Requested;
    public string? RejectReason { get; private set; }
    public bool AutoFromOrganizerReject { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }

    private RefundRequest()
    {
    }

    public static RefundRequest Create(
        Guid paymentId,
        Guid registrationId,
        Guid eventId,
        Guid userId,
        decimal paidAmount,
        decimal refundAmount,
        decimal refundPercent,
        string reason,
        bool autoFromOrganizerReject = false)
    {
        if (paymentId == Guid.Empty || registrationId == Guid.Empty)
        {
            throw new DomainException("Payment/Registration không hợp lệ.");
        }

        if (paidAmount <= 0 || refundAmount <= 0 || refundAmount > paidAmount)
        {
            throw new DomainException("Số tiền hoàn không hợp lệ (phải dựa trên số đã thanh toán).");
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new DomainException("Cần lý do hoàn tiền.");
        }

        var now = DateTime.UtcNow;
        return new RefundRequest
        {
            PaymentId = paymentId,
            RegistrationId = registrationId,
            EventId = eventId,
            UserId = userId,
            PaidAmount = paidAmount,
            RefundAmount = refundAmount,
            RefundPercent = refundPercent,
            Reason = reason.Trim(),
            Status = RefundRequestStatus.Requested,
            AutoFromOrganizerReject = autoFromOrganizerReject,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };
    }

    public void MarkProcessing()
    {
        Status = RefundRequestStatus.Processing;
        UpdatedAtUtc = DateTime.UtcNow;
    }

    public void MarkRefunded()
    {
        Status = RefundRequestStatus.Refunded;
        UpdatedAtUtc = DateTime.UtcNow;
    }

    public void Reject(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new DomainException("Cần lý do từ chối hoàn.");
        }

        Status = RefundRequestStatus.Rejected;
        RejectReason = reason.Trim();
        UpdatedAtUtc = DateTime.UtcNow;
    }
}
