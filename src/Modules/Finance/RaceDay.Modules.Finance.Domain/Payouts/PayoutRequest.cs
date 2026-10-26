using RaceDay.Shared.Domain.Abstractions;
using RaceDay.Shared.Domain.Exceptions;

namespace RaceDay.Modules.Finance.Domain.Payouts;

public enum PayoutRequestStatus
{
    Pending = 0,
    Approved = 1,
    Transferred = 2,
    Rejected = 3
}

public sealed class PayoutRequest : AggregateRoot
{
    public Guid EventId { get; private set; }
    public Guid OrganizerUserId { get; private set; }
    public string EventName { get; private set; } = string.Empty;
    public decimal Amount { get; private set; }
    public string? Note { get; private set; }
    public PayoutRequestStatus Status { get; private set; } = PayoutRequestStatus.Pending;
    public string? RejectReason { get; private set; }
    public string BankName { get; private set; } = string.Empty;
    public string AccountNumber { get; private set; } = string.Empty;
    public string AccountHolderName { get; private set; } = string.Empty;
    public string? Branch { get; private set; }
    public string? TransferNote { get; private set; }
    public string? ReceiptUrl { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }
    public DateTime? TransferredAtUtc { get; private set; }

    private PayoutRequest()
    {
    }

    public static PayoutRequest Create(
        Guid eventId,
        Guid organizerUserId,
        string eventName,
        decimal amount,
        string? note,
        string bankName,
        string accountNumber,
        string accountHolderName,
        string? branch,
        string? transferNote)
    {
        if (amount <= 0)
        {
            throw new DomainException("Số tiền rút không hợp lệ.");
        }

        var now = DateTime.UtcNow;
        return new PayoutRequest
        {
            EventId = eventId,
            OrganizerUserId = organizerUserId,
            EventName = eventName.Trim(),
            Amount = amount,
            Note = note?.Trim(),
            Status = PayoutRequestStatus.Pending,
            BankName = bankName.Trim(),
            AccountNumber = accountNumber.Trim(),
            AccountHolderName = accountHolderName.Trim(),
            Branch = branch?.Trim(),
            TransferNote = transferNote?.Trim(),
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };
    }

    public void MarkTransferred(string? receiptUrl)
    {
        if (Status is not (PayoutRequestStatus.Pending or PayoutRequestStatus.Approved))
        {
            throw new DomainException("Chỉ duyệt lệnh đang chờ.");
        }

        Status = PayoutRequestStatus.Transferred;
        ReceiptUrl = string.IsNullOrWhiteSpace(receiptUrl) ? ReceiptUrl : receiptUrl.Trim();
        TransferredAtUtc = DateTime.UtcNow;
        UpdatedAtUtc = DateTime.UtcNow;
    }

    public void Reject(string reason)
    {
        if (Status != PayoutRequestStatus.Pending)
        {
            throw new DomainException("Chỉ từ chối lệnh đang chờ.");
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new DomainException("Cần lý do từ chối.");
        }

        Status = PayoutRequestStatus.Rejected;
        RejectReason = reason.Trim();
        UpdatedAtUtc = DateTime.UtcNow;
    }
}
