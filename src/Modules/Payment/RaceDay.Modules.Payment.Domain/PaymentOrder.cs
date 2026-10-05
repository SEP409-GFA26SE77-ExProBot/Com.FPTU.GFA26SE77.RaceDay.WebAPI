using RaceDay.Shared.Domain.Abstractions;
using RaceDay.Shared.Domain.Exceptions;

namespace RaceDay.Modules.Payment.Domain;

public enum PaymentStatus
{
    Pending = 0,
    Paid = 1,
    Failed = 2,
    Cancelled = 3,
    Expired = 4,
    Refunded = 5
}

public enum PaymentTransactionStatus
{
    Created = 0,
    Succeeded = 1,
    Failed = 2
}

public sealed class PaymentTransaction : Entity
{
    public Guid PaymentId { get; private set; }
    public string TransactionCode { get; private set; } = string.Empty;
    public string? GatewayTransactionId { get; private set; }
    public string TransactionType { get; private set; } = "charge";
    public PaymentTransactionStatus Status { get; private set; }
    public decimal Amount { get; private set; }
    public string? RawResponseJson { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }

    private PaymentTransaction()
    {
    }

    public static PaymentTransaction Create(
        Guid paymentId,
        string transactionCode,
        string? gatewayTransactionId,
        string transactionType,
        PaymentTransactionStatus status,
        decimal amount,
        string? rawResponseJson)
    {
        return new PaymentTransaction
        {
            PaymentId = paymentId,
            TransactionCode = transactionCode,
            GatewayTransactionId = gatewayTransactionId,
            TransactionType = transactionType,
            Status = status,
            Amount = amount,
            RawResponseJson = rawResponseJson,
            CreatedAtUtc = DateTime.UtcNow
        };
    }
}

public sealed class PaymentOrder : AggregateRoot
{
    private readonly List<PaymentTransaction> _transactions = [];

    public Guid RegistrationId { get; private set; }
    public Guid UserId { get; private set; }
    public decimal Amount { get; private set; }
    public string Currency { get; private set; } = "VND";
    public string? PaymentMethod { get; private set; }
    public PaymentStatus Status { get; private set; } = PaymentStatus.Pending;
    public string? PaymentGateway { get; private set; }
    public string? TransactionReference { get; private set; }
    public long OrderCode { get; private set; }
    public string? CheckoutUrl { get; private set; }
    public DateTime? PaidAtUtc { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }

    public IReadOnlyCollection<PaymentTransaction> Transactions => _transactions.AsReadOnly();

    private PaymentOrder()
    {
    }

    public static PaymentOrder Create(
        Guid registrationId,
        Guid userId,
        decimal amount,
        string currency,
        long orderCode)
    {
        if (registrationId == Guid.Empty || userId == Guid.Empty)
        {
            throw new DomainException("Tham chiếu thanh toán không hợp lệ.");
        }

        if (amount <= 0)
        {
            throw new DomainException("Số tiền phải > 0.");
        }

        var now = DateTime.UtcNow;
        return new PaymentOrder
        {
            RegistrationId = registrationId,
            UserId = userId,
            Amount = amount,
            Currency = string.IsNullOrWhiteSpace(currency) ? "VND" : currency.Trim().ToUpperInvariant(),
            PaymentMethod = "PayOS",
            PaymentGateway = "PayOS",
            Status = PaymentStatus.Pending,
            OrderCode = orderCode,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };
    }

    public void AttachCheckout(string checkoutUrl, string? transactionReference)
    {
        if (Status != PaymentStatus.Pending)
        {
            throw new DomainException("Chỉ gắn checkout khi payment đang Pending.");
        }

        CheckoutUrl = checkoutUrl;
        TransactionReference = transactionReference;
        Touch();
    }

    public void AddTransaction(PaymentTransaction tx)
    {
        _transactions.Add(tx);
        Touch();
    }

    public PaymentTransaction MarkPaid(string? gatewayTransactionId, string? rawJson)
    {
        if (Status == PaymentStatus.Paid)
        {
            return _transactions.Last(t => t.Status == PaymentTransactionStatus.Succeeded);
        }

        if (Status is not (PaymentStatus.Pending or PaymentStatus.Failed))
        {
            throw new DomainException($"Không thể mark paid từ trạng thái {Status}.");
        }

        Status = PaymentStatus.Paid;
        PaidAtUtc = DateTime.UtcNow;
        if (!string.IsNullOrWhiteSpace(gatewayTransactionId))
        {
            TransactionReference = gatewayTransactionId;
        }

        var tx = PaymentTransaction.Create(
            Id,
            $"pay-{OrderCode}",
            gatewayTransactionId,
            "charge",
            PaymentTransactionStatus.Succeeded,
            Amount,
            rawJson);
        _transactions.Add(tx);
        Touch();
        return tx;
    }

    public PaymentTransaction MarkFailed(string reason, string? rawJson)
    {
        if (Status == PaymentStatus.Paid)
        {
            throw new DomainException("Payment đã paid, không mark failed.");
        }

        Status = PaymentStatus.Failed;
        var tx = PaymentTransaction.Create(
            Id,
            $"fail-{OrderCode}",
            null,
            "charge",
            PaymentTransactionStatus.Failed,
            Amount,
            rawJson ?? reason);
        _transactions.Add(tx);
        Touch();
        return tx;
    }

    public void MarkCancelled()
    {
        if (Status == PaymentStatus.Paid)
        {
            throw new DomainException("Không hủy payment đã paid.");
        }

        Status = PaymentStatus.Cancelled;
        Touch();
    }

    public void MarkRefunded()
    {
        if (Status != PaymentStatus.Paid)
        {
            throw new DomainException("Chỉ refund payment đã paid.");
        }

        Status = PaymentStatus.Refunded;
        Touch();
    }

    private void Touch() => UpdatedAtUtc = DateTime.UtcNow;
}
