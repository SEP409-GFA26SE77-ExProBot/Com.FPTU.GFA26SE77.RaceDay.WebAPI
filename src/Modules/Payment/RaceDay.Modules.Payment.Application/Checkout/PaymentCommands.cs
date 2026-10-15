using FluentValidation;
using MediatR;
using RaceDay.Modules.Event.Contracts.Registrations;
using RaceDay.Modules.Payment.Contracts.Events;
using RaceDay.Modules.Payment.Domain;
using RaceDay.Shared.Application.Abstractions;
using RaceDay.Shared.Application.Messaging;
using RaceDay.Shared.Application.Validation;
using RaceDay.Shared.Domain.Exceptions;

namespace RaceDay.Modules.Payment.Application;

public interface IPaymentRepository
{
    Task<PaymentOrder?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<PaymentOrder?> GetByOrderCodeAsync(long orderCode, CancellationToken cancellationToken = default);
    Task<PaymentOrder?> GetPendingByRegistrationAsync(Guid registrationId, CancellationToken cancellationToken = default);
    Task<PaymentOrder?> GetPendingByReferenceAsync(PaymentPurpose purpose, Guid referenceId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<PaymentOrder>> ListByUserAsync(Guid userId, CancellationToken cancellationToken = default);
    Task AddAsync(PaymentOrder payment, CancellationToken cancellationToken = default);
    void AddTransaction(PaymentTransaction transaction);
    void Update(PaymentOrder payment);
}

public interface IPaymentUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}

public interface IPayOsGateway
{
    bool IsMock { get; }
    Task<PayOsCreateResult> CreatePaymentLinkAsync(PayOsCreateRequest request, CancellationToken cancellationToken = default);
    Task<PayOsPaymentLinkInfo?> GetPaymentLinkAsync(long orderCode, CancellationToken cancellationToken = default);
    /// <summary>
    /// Hoàn tiền đã paid: mock → ghi nhận ngay; live → lệnh chi PayOS về STK đối ứng (webhook).
    /// </summary>
    Task<PayOsRefundResult> RefundAsync(PayOsRefundRequest request, CancellationToken cancellationToken = default);
    bool VerifyWebhookSignature(string rawBody, string? signatureHeader);
}

public sealed record PayOsCreateRequest(
    long OrderCode,
    int AmountVnd,
    string Description,
    string ReturnUrl,
    string CancelUrl,
    Guid PaymentId);

public sealed record PayOsCreateResult(
    string CheckoutUrl,
    string? PaymentLinkId,
    string? RawResponseJson,
    string? QrCode = null,
    string? AccountNumber = null,
    string? AccountName = null,
    string? Bin = null,
    string? TransferDescription = null);

public sealed record PayOsPaymentLinkInfo(
    long OrderCode,
    string? Status,
    string? PaymentLinkId,
    string? RawJson);

public sealed record PayOsRefundRequest(
    Guid PaymentId,
    long OrderCode,
    int AmountVnd,
    string Description,
    string? CounterAccountBankId,
    string? CounterAccountNumber,
    string? ChargeRawJson);

public sealed record PayOsRefundResult(
    bool IsMock,
    string? PayoutId,
    string? RawResponseJson);

public sealed record PaymentDto(
    Guid Id,
    string Purpose,
    Guid? RegistrationId,
    Guid? ReferenceId,
    Guid UserId,
    decimal Amount,
    decimal? ListAmount,
    decimal? DiscountAmount,
    string? PromoCode,
    string Currency,
    string Status,
    string? PaymentMethod,
    string? PaymentGateway,
    string? TransactionReference,
    long OrderCode,
    string? CheckoutUrl,
    DateTime? PaidAtUtc,
    DateTime CreatedAtUtc,
    string? QrCode = null,
    string? AccountNumber = null,
    string? AccountName = null,
    string? Bin = null,
    string? TransferDescription = null);

public static class PaymentDtoMapper
{
    public static string ToApi(PaymentStatus s) => s switch
    {
        PaymentStatus.Pending => "pending",
        PaymentStatus.Paid => "paid",
        PaymentStatus.Failed => "failed",
        PaymentStatus.Cancelled => "cancelled",
        PaymentStatus.Expired => "expired",
        PaymentStatus.Refunded => "refunded",
        _ => s.ToString().ToLowerInvariant()
    };

    public static string ToApiPurpose(PaymentPurpose p) => p switch
    {
        PaymentPurpose.Registration => "registration",
        PaymentPurpose.EventPackage => "event_package",
        _ => p.ToString().ToLowerInvariant()
    };

    public static PaymentDto ToDto(PaymentOrder p, PayOsCreateResult? payOs = null) => new(
        p.Id, ToApiPurpose(p.Purpose), p.RegistrationId, p.ReferenceId,
        p.UserId, p.Amount, p.ListAmount, p.DiscountAmount, p.PromoCode,
        p.Currency, ToApi(p.Status),
        p.PaymentMethod, p.PaymentGateway, p.TransactionReference, p.OrderCode,
        p.CheckoutUrl, p.PaidAtUtc, p.CreatedAtUtc,
        payOs?.QrCode, payOs?.AccountNumber, payOs?.AccountName, payOs?.Bin,
        payOs?.TransferDescription);

    /// <summary>Lấy VietQR fields từ raw PayOS create (khi reuse checkoutUrl cũ).</summary>
    public static PayOsCreateResult? TryParseVietQrFromRaw(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(raw);
            var root = doc.RootElement;
            var data = root.TryGetProperty("data", out var d) ? d : root;
            var qr = data.TryGetProperty("qrCode", out var q) ? q.GetString() : null;
            if (string.IsNullOrWhiteSpace(qr)) return null;
            return new PayOsCreateResult(
                CheckoutUrl: data.TryGetProperty("checkoutUrl", out var cu) ? cu.GetString() ?? "" : "",
                PaymentLinkId: data.TryGetProperty("paymentLinkId", out var pl) ? pl.GetString() : null,
                RawResponseJson: raw,
                QrCode: qr,
                AccountNumber: data.TryGetProperty("accountNumber", out var an) ? an.GetString() : null,
                AccountName: data.TryGetProperty("accountName", out var am) ? am.GetString() : null,
                Bin: data.TryGetProperty("bin", out var b) ? b.GetString() : null,
                TransferDescription: data.TryGetProperty("description", out var td) ? td.GetString() : null);
        }
        catch
        {
            return null;
        }
    }
}

public sealed record CreateCheckoutCommand(
    Guid RegistrationId,
    string ReturnUrl,
    string CancelUrl) : IRequest<PaymentDto>;

public sealed class CreateCheckoutCommandValidator : AbstractValidator<CreateCheckoutCommand>
{
    public CreateCheckoutCommandValidator()
    {
        RuleFor(x => x.RegistrationId).NotEmpty();
        RuleFor(x => x.ReturnUrl).NotEmpty().MaximumLength(500).OptionalHttpUrl();
        RuleFor(x => x.CancelUrl).NotEmpty().MaximumLength(500).OptionalHttpUrl();
    }
}

public sealed class CreateCheckoutCommandHandler(
    IRegistrationCheckoutInfoQuery checkoutInfo,
    IPaymentRepository payments,
    IPayOsGateway payOs,
    ICurrentUserAccessor currentUser,
    IPaymentUnitOfWork unitOfWork) : IRequestHandler<CreateCheckoutCommand, PaymentDto>
{
    public async Task<PaymentDto> Handle(CreateCheckoutCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new DomainException("Cáº§n Ä‘Äƒng nháº­p.");
        var info = await checkoutInfo.GetAsync(request.RegistrationId, cancellationToken)
            ?? throw new DomainException("KhĂ´ng tĂ¬m tháº¥y Ä‘Äƒng kĂ½.");

        if (info.UserId != userId && !currentUser.IsAdmin)
        {
            throw new DomainException("KhĂ´ng thá»ƒ thanh toĂ¡n Ä‘Äƒng kĂ½ cá»§a ngÆ°á»i khĂ¡c.");
        }

        if (!string.Equals(info.RegistrationStatus, "pending_payment", StringComparison.OrdinalIgnoreCase))
        {
            throw new DomainException($"ÄÄƒng kĂ½ khĂ´ng á»Ÿ tráº¡ng thĂ¡i chá» thanh toĂ¡n (hiá»‡n: {info.RegistrationStatus}).");
        }

        var existing = await payments.GetPendingByRegistrationAsync(request.RegistrationId, cancellationToken);
        if (existing is { CheckoutUrl: not null })
        {
            var createRaw = existing.Transactions
                .OrderByDescending(t => t.CreatedAtUtc)
                .FirstOrDefault(t => t.TransactionType == "create_link")
                ?.RawResponseJson;
            var vietQr = PaymentDtoMapper.TryParseVietQrFromRaw(createRaw);
            return PaymentDtoMapper.ToDto(existing, vietQr);
        }

        var orderCode = existing?.OrderCode ?? GenerateOrderCode();
        var payment = existing ?? PaymentOrder.Create(
            request.RegistrationId,
            info.UserId,
            info.Amount,
            info.Currency,
            orderCode);

        if (existing is null)
        {
            await payments.AddAsync(payment, cancellationToken);
        }

        var amountVnd = (int)Math.Round(info.Amount, MidpointRounding.AwayFromZero);
        if (amountVnd < 1000)
        {
            throw new DomainException("Sá»‘ tiá»n PayOS tá»‘i thiá»ƒu 1000 VND.");
        }

        // Live PayOS: description max 9 chars for non-linked bank accounts.
        var desc = string.IsNullOrWhiteSpace(info.DistanceType) ? "RaceDay" : info.DistanceType.Trim();

        var created = await payOs.CreatePaymentLinkAsync(
            new PayOsCreateRequest(
                payment.OrderCode,
                amountVnd,
                desc,
                request.ReturnUrl,
                request.CancelUrl,
                payment.Id),
            cancellationToken);

        payment.AttachCheckout(created.CheckoutUrl, created.PaymentLinkId);
        var createTx = PaymentTransaction.Create(
            payment.Id,
            $"create-{payment.OrderCode}",
            created.PaymentLinkId,
            "create_link",
            PaymentTransactionStatus.Created,
            payment.Amount,
            created.RawResponseJson);
        payment.AddTransaction(createTx);
        // Explicit Add: client-generated Guid children are otherwise UPDATEd (0 rows → 500).
        payments.AddTransaction(createTx);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        // Trả VietQR + CK để FE vẽ 1 khung (không dùng tab PayOS hosted)
        return PaymentDtoMapper.ToDto(payment, created);
    }

    private static long GenerateOrderCode()
    {
        // PayOS orderCode: unique positive int64-ish; use time + random.
        var ms = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() % 1_000_000_000_000L;
        var rnd = Random.Shared.Next(100, 999);
        return ms * 1000 + rnd;
    }
}

public sealed record ConfirmPaymentPaidCommand(
    Guid? PaymentId,
    long? OrderCode,
    string? GatewayTransactionId,
    string? RawJson) : IRequest<PaymentDto>;

public sealed class ConfirmPaymentPaidCommandHandler(
    IPaymentRepository payments,
    IPaymentUnitOfWork unitOfWork,
    IPublisher publisher) : IRequestHandler<ConfirmPaymentPaidCommand, PaymentDto>
{
    public async Task<PaymentDto> Handle(ConfirmPaymentPaidCommand request, CancellationToken cancellationToken)
    {
        PaymentOrder? payment = null;
        if (request.PaymentId is Guid id)
        {
            payment = await payments.GetByIdAsync(id, cancellationToken);
        }
        else if (request.OrderCode is long code)
        {
            payment = await payments.GetByOrderCodeAsync(code, cancellationToken);
        }

        if (payment is null)
        {
            throw new DomainException("Không tìm thấy payment.");
        }

        var alreadyPaid = payment.Status == PaymentStatus.Paid;
        var tx = payment.MarkPaid(request.GatewayTransactionId, request.RawJson);
        if (!alreadyPaid)
        {
            payments.AddTransaction(tx);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        if (!alreadyPaid)
        {
            if (payment.Purpose == PaymentPurpose.EventPackage && payment.ReferenceId.HasValue)
            {
                await publisher.Publish(
                    new IntegrationEventOccurred(
                        new PackagePaymentCompletedIntegrationEvent(
                            payment.Id,
                            payment.ReferenceId.Value,
                            payment.UserId,
                            payment.Amount,
                            payment.Currency,
                            payment.PaymentGateway ?? "PayOS",
                            payment.TransactionReference)),
                    cancellationToken);
            }
            else if (payment.Purpose == PaymentPurpose.Registration && payment.RegistrationId.HasValue)
            {
                await publisher.Publish(
                    new IntegrationEventOccurred(
                        new PaymentCompletedIntegrationEvent(
                            payment.Id,
                            payment.RegistrationId.Value,
                            payment.UserId,
                            payment.Amount,
                            payment.Currency,
                            payment.PaymentGateway ?? "PayOS",
                            payment.TransactionReference)),
                    cancellationToken);
            }
        }

        return PaymentDtoMapper.ToDto(payment);
    }
}

public sealed record ListMyPaymentsQuery : IRequest<IReadOnlyList<PaymentDto>>;

public sealed class ListMyPaymentsQueryHandler(
    IPaymentRepository payments,
    ICurrentUserAccessor currentUser) : IRequestHandler<ListMyPaymentsQuery, IReadOnlyList<PaymentDto>>
{
    public async Task<IReadOnlyList<PaymentDto>> Handle(ListMyPaymentsQuery request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new DomainException("Cáº§n Ä‘Äƒng nháº­p.");
        var items = await payments.ListByUserAsync(userId, cancellationToken);
        return items.Select(p => PaymentDtoMapper.ToDto(p)).ToList();
    }
}

public sealed record GetPaymentByIdQuery(Guid PaymentId) : IRequest<PaymentDto>;

public sealed class GetPaymentByIdQueryHandler(
    IPaymentRepository payments,
    ICurrentUserAccessor currentUser) : IRequestHandler<GetPaymentByIdQuery, PaymentDto>
{
    public async Task<PaymentDto> Handle(GetPaymentByIdQuery request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new DomainException("Cáº§n Ä‘Äƒng nháº­p.");
        var payment = await payments.GetByIdAsync(request.PaymentId, cancellationToken)
            ?? throw new DomainException("KhĂ´ng tĂ¬m tháº¥y payment.");
        if (payment.UserId != userId && !currentUser.IsAdmin)
        {
            throw new DomainException("KhĂ´ng cĂ³ quyá»n xem payment nĂ y.");
        }

        return PaymentDtoMapper.ToDto(payment);
    }
}


/// <summary>
/// Poll PayOS for PAID — dùng khi test local chưa có webhook public (ngrok).
/// FE gọi sau khi user quay về returnUrl.
/// </summary>
public sealed record SyncPaymentFromPayOsCommand(Guid PaymentId) : IRequest<PaymentDto>;

public sealed class SyncPaymentFromPayOsCommandHandler(
    IPaymentRepository payments,
    IPayOsGateway payOs,
    IMediator mediator,
    ICurrentUserAccessor currentUser) : IRequestHandler<SyncPaymentFromPayOsCommand, PaymentDto>
{
    public async Task<PaymentDto> Handle(SyncPaymentFromPayOsCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new DomainException("Can dang nhap.");
        var payment = await payments.GetByIdAsync(request.PaymentId, cancellationToken)
            ?? throw new DomainException("Khong tim thay payment.");

        if (payment.UserId != userId && !currentUser.IsAdmin)
        {
            throw new DomainException("Khong co quyen sync payment nay.");
        }

        if (payment.Status == PaymentStatus.Paid)
        {
            return PaymentDtoMapper.ToDto(payment);
        }

        // Mock ≈ live: FE luôn gọi sync sau returnUrl. Mock đã PAID qua /mock-checkout/.../pay
        // (cùng ConfirmPaymentPaid + webhook-equivalent). Sync chỉ đọc local status.
        if (payOs.IsMock)
        {
            return PaymentDtoMapper.ToDto(payment);
        }

        var info = await payOs.GetPaymentLinkAsync(payment.OrderCode, cancellationToken)
            ?? throw new DomainException("Không lấy được trạng thái từ PayOS.");

        if (!string.Equals(info.Status, "PAID", StringComparison.OrdinalIgnoreCase))
        {
            return PaymentDtoMapper.ToDto(payment);
        }

        return await mediator.Send(
            new ConfirmPaymentPaidCommand(payment.Id, payment.OrderCode, info.PaymentLinkId, info.RawJson),
            cancellationToken);
    }
}

// ── Package checkout ──────────────────────────────────────────────

public sealed record CreatePackageCheckoutCommand(
    Guid PackagePurchaseId,
    string ReturnUrl,
    string CancelUrl) : IRequest<PaymentDto>;

public sealed class CreatePackageCheckoutCommandValidator : AbstractValidator<CreatePackageCheckoutCommand>
{
    public CreatePackageCheckoutCommandValidator()
    {
        RuleFor(x => x.PackagePurchaseId).NotEmpty();
        RuleFor(x => x.ReturnUrl).NotEmpty().MaximumLength(500).OptionalHttpUrl();
        RuleFor(x => x.CancelUrl).NotEmpty().MaximumLength(500).OptionalHttpUrl();
    }
}

public sealed class CreatePackageCheckoutCommandHandler(
    RaceDay.Modules.Event.Contracts.Packages.IPackageCheckoutInfoQuery checkoutInfo,
    IPaymentRepository payments,
    IPayOsGateway payOs,
    ICurrentUserAccessor currentUser,
    IPaymentUnitOfWork unitOfWork) : IRequestHandler<CreatePackageCheckoutCommand, PaymentDto>
{
    public async Task<PaymentDto> Handle(CreatePackageCheckoutCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new DomainException("Cần đăng nhập.");
        var info = await checkoutInfo.GetAsync(request.PackagePurchaseId, cancellationToken)
            ?? throw new DomainException("Không tìm thấy gói mua.");

        if (info.UserId != userId && !currentUser.IsAdmin)
        {
            throw new DomainException("Không thể thanh toán gói của người khác.");
        }

        if (!string.Equals(info.Status, "pending_payment", StringComparison.OrdinalIgnoreCase))
        {
            throw new DomainException($"Gói không ở trạng thái chờ thanh toán (hiện: {info.Status}).");
        }

        var existing = await payments.GetPendingByReferenceAsync(
            PaymentPurpose.EventPackage, request.PackagePurchaseId, cancellationToken);
        if (existing is { CheckoutUrl: not null })
        {
            var createRaw = existing.Transactions
                .OrderByDescending(t => t.CreatedAtUtc)
                .FirstOrDefault(t => t.TransactionType == "create_link")
                ?.RawResponseJson;
            var vietQr = PaymentDtoMapper.TryParseVietQrFromRaw(createRaw);
            return PaymentDtoMapper.ToDto(existing, vietQr);
        }

        var orderCode = existing?.OrderCode ?? GenerateOrderCode();
        var payment = existing ?? PaymentOrder.CreateForPackage(
            request.PackagePurchaseId,
            info.UserId,
            info.ListAmount,
            info.DiscountAmount,
            info.PromoCode,
            info.Currency,
            orderCode);

        if (existing is null)
        {
            await payments.AddAsync(payment, cancellationToken);
        }

        var amountVnd = (int)Math.Round(info.TotalAmount, MidpointRounding.AwayFromZero);
        if (amountVnd < 1000)
        {
            throw new DomainException("Số tiền PayOS tối thiểu 1000 VND.");
        }

        var desc = "RaceDay";

        var created = await payOs.CreatePaymentLinkAsync(
            new PayOsCreateRequest(
                payment.OrderCode,
                amountVnd,
                desc,
                request.ReturnUrl,
                request.CancelUrl,
                payment.Id),
            cancellationToken);

        payment.AttachCheckout(created.CheckoutUrl, created.PaymentLinkId);
        var createTx = PaymentTransaction.Create(
            payment.Id,
            $"create-{payment.OrderCode}",
            created.PaymentLinkId,
            "create_link",
            PaymentTransactionStatus.Created,
            payment.Amount,
            created.RawResponseJson);
        payment.AddTransaction(createTx);
        payments.AddTransaction(createTx);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return PaymentDtoMapper.ToDto(payment, created);
    }

    private static long GenerateOrderCode()
    {
        var ms = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() % 1_000_000_000_000L;
        var rnd = Random.Shared.Next(100, 999);
        return ms * 1000 + rnd;
    }
}
