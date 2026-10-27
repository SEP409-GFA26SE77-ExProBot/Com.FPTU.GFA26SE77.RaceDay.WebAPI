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

public enum PaymentPurpose
{
    Registration = 0,
    EventPackage = 1
}

public sealed class PaymentOrder : AggregateRoot
{
    private readonly List<PaymentTransaction> _transactions = [];

    public PaymentPurpose Purpose { get; private set; } = PaymentPurpose.Registration;
    public Guid? RegistrationId { get; private set; }
    public Guid? ReferenceId { get; private set; }
    public Guid UserId { get; private set; }
    public decimal Amount { get; private set; }
    public decimal? ListAmount { get; private set; }
    public decimal? DiscountAmount { get; private set; }
    public string? PromoCode { get; private set; }
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
            Purpose = PaymentPurpose.Registration,
            RegistrationId = registrationId,
            UserId = userId,
            Amount = amount,
            ListAmount = amount,
            DiscountAmount = 0,
            PromoCode = null,
            Currency = string.IsNullOrWhiteSpace(currency) ? "VND" : currency.Trim().ToUpperInvariant(),
            PaymentMethod = "PayOS",
            PaymentGateway = "PayOS",
            Status = PaymentStatus.Pending,
            OrderCode = orderCode,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };
    }

    public static PaymentOrder CreateForPackage(
        Guid referenceId,
        Guid userId,
        decimal listAmount,
        decimal discountAmount,
        string? promoCode,
        string currency,
        long orderCode)
    {
        if (referenceId == Guid.Empty || userId == Guid.Empty)
        {
            throw new DomainException("Tham chiếu thanh toán không hợp lệ.");
        }

        var charged = listAmount - discountAmount;
        if (charged <= 0)
        {
            throw new DomainException("Số tiền phải > 0.");
        }

        var now = DateTime.UtcNow;
        return new PaymentOrder
        {
            Purpose = PaymentPurpose.EventPackage,
            ReferenceId = referenceId,
            RegistrationId = null,
            UserId = userId,
            Amount = charged,
            ListAmount = listAmount,
            DiscountAmount = discountAmount,
            PromoCode = promoCode?.Trim().ToUpperInvariant(),
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

        if (Status == PaymentStatus.Cancelled)
        {
            return;
        }

        Status = PaymentStatus.Cancelled;
        Touch();
    }

    /// <summary>PayOS link/QR hết hạn hoặc TTL nội bộ — không tái dùng CheckoutUrl.</summary>
    public void MarkExpired()
    {
        if (Status == PaymentStatus.Paid)
        {
            throw new DomainException("Không đánh hết hạn payment đã paid.");
        }

        if (Status is PaymentStatus.Expired or PaymentStatus.Cancelled or PaymentStatus.Refunded)
        {
            return;
        }

        Status = PaymentStatus.Expired;
        Touch();
    }

    public PaymentTransaction MarkRefunded(
        decimal refundAmount,
        string? gatewayTransactionId,
        string? rawJson)
    {
        if (Status == PaymentStatus.Refunded)
        {
            return _transactions.LastOrDefault(t =>
                       string.Equals(t.TransactionType, "refund", StringComparison.OrdinalIgnoreCase)
                       && t.Status == PaymentTransactionStatus.Succeeded)
                   ?? throw new DomainException("Payment đã refund nhưng thiếu transaction.");
        }

        if (Status != PaymentStatus.Paid)
        {
            throw new DomainException("Chỉ refund payment đã paid.");
        }

        if (refundAmount <= 0 || refundAmount > Amount)
        {
            throw new DomainException("Số tiền hoàn không hợp lệ.");
        }

        Status = PaymentStatus.Refunded;
        var tx = PaymentTransaction.Create(
            Id,
            $"ref-{OrderCode}",
            gatewayTransactionId,
            "refund",
            PaymentTransactionStatus.Succeeded,
            refundAmount,
            rawJson);
        _transactions.Add(tx);
        Touch();
        return tx;
    }

    private void Touch() => UpdatedAtUtc = DateTime.UtcNow;
}
