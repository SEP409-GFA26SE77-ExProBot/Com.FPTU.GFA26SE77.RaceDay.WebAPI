using FluentValidation;
using MediatR;
using RaceDay.Modules.Event.Contracts.Registrations;
using RaceDay.Modules.Payment.Contracts.Events;
using RaceDay.Modules.Payment.Domain;
using RaceDay.Shared.Application.Abstractions;
using RaceDay.Shared.Application.Messaging;
using RaceDay.Shared.Domain.Exceptions;

namespace RaceDay.Modules.Payment.Application;

public interface IPaymentRepository
{
    Task<PaymentOrder?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<PaymentOrder?> GetByOrderCodeAsync(long orderCode, CancellationToken cancellationToken = default);
    Task<PaymentOrder?> GetPendingByRegistrationAsync(Guid registrationId, CancellationToken cancellationToken = default);
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
    string? RawResponseJson);

public sealed record PayOsPaymentLinkInfo(
    long OrderCode,
    string? Status,
    string? PaymentLinkId,
    string? RawJson);

public sealed record PaymentDto(
    Guid Id,
    Guid RegistrationId,
    Guid UserId,
    decimal Amount,
    string Currency,
    string Status,
    string? PaymentMethod,
    string? PaymentGateway,
    string? TransactionReference,
    long OrderCode,
    string? CheckoutUrl,
    DateTime? PaidAtUtc,
    DateTime CreatedAtUtc);

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

    public static PaymentDto ToDto(PaymentOrder p) => new(
        p.Id, p.RegistrationId, p.UserId, p.Amount, p.Currency, ToApi(p.Status),
        p.PaymentMethod, p.PaymentGateway, p.TransactionReference, p.OrderCode,
        p.CheckoutUrl, p.PaidAtUtc, p.CreatedAtUtc);
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
        RuleFor(x => x.ReturnUrl).NotEmpty().MaximumLength(500);
        RuleFor(x => x.CancelUrl).NotEmpty().MaximumLength(500);
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
            return PaymentDtoMapper.ToDto(existing);
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
        return PaymentDtoMapper.ToDto(payment);
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
            // In-process notify Event module (monolith). Avoid IAM outbox without SaveChanges.
            await publisher.Publish(
                new IntegrationEventOccurred(
                    new PaymentCompletedIntegrationEvent(
                        payment.Id,
                        payment.RegistrationId,
                        payment.UserId,
                        payment.Amount,
                        payment.Currency,
                        payment.PaymentGateway ?? "PayOS",
                        payment.TransactionReference)),
                cancellationToken);
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
        return items.Select(PaymentDtoMapper.ToDto).ToList();
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
