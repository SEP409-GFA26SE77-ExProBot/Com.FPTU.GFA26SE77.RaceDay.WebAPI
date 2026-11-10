using FluentValidation;
using MediatR;
using Microsoft.Extensions.Logging;
using RaceDay.Modules.Event.Contracts.Finance;
using RaceDay.Modules.Finance.Application.Abstractions;
using RaceDay.Modules.Finance.Application.Wallet;
using RaceDay.Modules.Finance.Contracts.Events;
using RaceDay.Modules.Finance.Domain.Refunds;
using RaceDay.Modules.Finance.Domain.Settings;
using RaceDay.Modules.Payment.Contracts.Payments;
using RaceDay.Shared.Application.Abstractions;
using RaceDay.Shared.Application.Messaging;
using RaceDay.Shared.Domain.Exceptions;

namespace RaceDay.Modules.Finance.Application.Refunds;

public sealed record ListMyRefundsQuery : IRequest<IReadOnlyList<RefundRequestDto>>;

public sealed class ListMyRefundsQueryHandler(
    IRefundRequestRepository refunds,
    ICurrentUserAccessor currentUser) : IRequestHandler<ListMyRefundsQuery, IReadOnlyList<RefundRequestDto>>
{
    public async Task<IReadOnlyList<RefundRequestDto>> Handle(ListMyRefundsQuery request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new DomainException("Cần đăng nhập.");
        var list = await refunds.ListByUserAsync(userId, cancellationToken);
        return list.Select(Map).ToList();
    }

    internal static RefundRequestDto Map(RefundRequest r) => new(
        r.Id,
        r.PaymentId,
        r.RegistrationId,
        r.EventId,
        r.RefundAmount,
        r.Reason,
        r.Status.ToString().ToLowerInvariant(),
        r.CreatedAtUtc,
        r.UpdatedAtUtc,
        r.RejectReason,
        r.IsSystemErrorClaim,
        r.PaidAmount,
        r.RefundPercent,
        r.UserId);

    internal static ManagerSystemErrorRefundDto MapManager(
        RefundRequest r,
        RegistrationFinanceInfo? reg = null)
    {
        var (reasonText, evidence) = SplitSystemErrorReason(r.Reason);
        return new ManagerSystemErrorRefundDto(
            r.Id,
            r.PaymentId,
            r.RegistrationId,
            r.EventId,
            r.UserId,
            r.PaidAmount,
            r.RefundAmount,
            r.RefundPercent,
            r.Reason,
            r.Status.ToString().ToLowerInvariant(),
            r.CreatedAtUtc,
            r.UpdatedAtUtc,
            r.RejectReason,
            reg?.EventName,
            reg?.Status,
            reg?.FullName,
            reg?.Email,
            reg?.BibNumber,
            reasonText,
            evidence);
    }

    internal static (string ReasonText, IReadOnlyList<string> EvidenceUrls) SplitSystemErrorReason(string raw)
    {
        var text = (raw ?? string.Empty).Trim();
        const string prefix = CreateRefundCommandHandler.SystemErrorReasonPrefix;
        if (text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            text = text[prefix.Length..].TrimStart();
        }

        var evidence = new List<string>();
        var idx = text.IndexOf("Đính kèm:", StringComparison.OrdinalIgnoreCase);
        string reasonText;
        if (idx >= 0)
        {
            reasonText = text[..idx].Trim();
            var block = text[idx..];
            foreach (var line in block.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var u = line.Trim().TrimStart('-', '•', '*').Trim();
                if (u.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                    || u.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                {
                    evidence.Add(u);
                }
            }
        }
        else
        {
            reasonText = text;
        }

        return (reasonText, evidence);
    }
}

public sealed record GetRefundQuoteQuery(Guid PaymentId, bool SystemErrorClaim = false) : IRequest<RefundQuoteDto>;

public sealed class GetRefundQuoteQueryHandler(
    IPaymentFinancePort payments,
    IEventFinanceQuery events,
    ICurrentUserAccessor currentUser) : IRequestHandler<GetRefundQuoteQuery, RefundQuoteDto>
{
    public async Task<RefundQuoteDto> Handle(GetRefundQuoteQuery request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new DomainException("Cần đăng nhập.");
        var payment = await payments.GetByIdAsync(request.PaymentId, cancellationToken)
            ?? throw new DomainException("Không tìm thấy payment.");
        if (payment.UserId != userId && !currentUser.IsAdmin && !currentUser.IsManager)
        {
            throw new DomainException("Payment không thuộc về bạn.");
        }

        var reg = await events.GetRegistrationAsync(payment.RegistrationId, cancellationToken)
            ?? throw new DomainException("Không tìm thấy đăng ký.");
        var ev = await events.GetEventAsync(reg.EventId, cancellationToken)
            ?? throw new DomainException("Không tìm thấy giải.");
        var days = LedgerCalculator.DaysUntilEvent(ev);

        if (request.SystemErrorClaim)
        {
            return new RefundQuoteDto(
                payment.PaymentId,
                payment.RegistrationId,
                reg.EventId,
                payment.Amount,
                100m,
                payment.Amount,
                0,
                "Khiếu nại lỗi hệ thống — đề xuất hoàn 100%. Manager/Admin xác minh trước khi hoàn thật.",
                days);
        }

        // Self-service: chỉ hoàn qua khiếu nại lỗi hệ thống (duyệt). Lý do cá nhân = 0%.
        return new RefundQuoteDto(
            payment.PaymentId,
            payment.RegistrationId,
            reg.EventId,
            payment.Amount,
            0,
            0,
            payment.Amount,
            "Vé không hoàn vì lý do cá nhân (ốm, đổi ý, trùng lịch…). Chỉ khiếu nại lỗi hệ thống mới được xét hoàn 100% sau khi Manager/Admin duyệt.",
            days);
    }
}

public sealed record CreateRefundCommand(CreateRefundRequestDto Body) : IRequest<RefundRequestDto>;

public sealed class CreateRefundCommandValidator : AbstractValidator<CreateRefundCommand>
{
    public CreateRefundCommandValidator()
    {
        RuleFor(x => x.Body.PaymentId).NotEmpty();
        RuleFor(x => x.Body.Reason).NotEmpty().MaximumLength(1000);
        RuleFor(x => x.Body.Reason)
            .MinimumLength(20)
            .When(x => x.Body.SystemErrorClaim)
            .WithMessage("Khiếu nại lỗi hệ thống cần mô tả cụ thể (≥20 ký tự).");
    }
}

public sealed class CreateRefundCommandHandler(
    IRefundRequestRepository refunds,
    IPaymentFinancePort payments,
    IEventFinanceQuery events,
    IFinanceUnitOfWork uow,
    ICurrentUserAccessor currentUser) : IRequestHandler<CreateRefundCommand, RefundRequestDto>
{
    public const string SystemErrorReasonPrefix = "[Lỗi hệ thống]";

    public async Task<RefundRequestDto> Handle(CreateRefundCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new DomainException("Cần đăng nhập.");
        var payment = await payments.GetByIdAsync(request.Body.PaymentId, cancellationToken)
            ?? throw new DomainException("Không tìm thấy payment.");
        if (payment.UserId != userId && !currentUser.IsAdmin)
        {
            throw new DomainException("Payment không thuộc về bạn.");
        }

        if (!string.Equals(payment.Status, "Paid", StringComparison.OrdinalIgnoreCase))
        {
            throw new DomainException("Chỉ hoàn được payment đã thanh toán.");
        }

        if (await refunds.HasOpenForPaymentAsync(payment.PaymentId, cancellationToken))
        {
            throw new DomainException("Đã có yêu cầu hoàn tiền cho giao dịch này.");
        }

        var reg = await events.GetRegistrationAsync(payment.RegistrationId, cancellationToken)
            ?? throw new DomainException("Không tìm thấy đăng ký.");
        _ = await events.GetEventAsync(reg.EventId, cancellationToken)
            ?? throw new DomainException("Không tìm thấy giải.");

        if (!request.Body.SystemErrorClaim)
        {
            throw new DomainException(
                "Không hoàn tiền theo lý do cá nhân. Chỉ gửi khiếu nại lỗi hệ thống — Manager/Admin duyệt rồi mới hoàn 100%.");
        }

        var detail = request.Body.Reason.Trim();
        if (detail.Length < 20)
        {
            throw new DomainException("Khiếu nại lỗi hệ thống cần mô tả cụ thể (≥20 ký tự).");
        }

        var reason = detail.StartsWith(SystemErrorReasonPrefix, StringComparison.OrdinalIgnoreCase)
            ? detail
            : $"{SystemErrorReasonPrefix} {detail}";

        var refund = RefundRequest.Create(
            payment.PaymentId,
            payment.RegistrationId,
            reg.EventId,
            payment.UserId,
            payment.Amount,
            payment.Amount,
            100m,
            reason,
            autoFromOrganizerReject: false,
            isSystemErrorClaim: true);

        await refunds.AddAsync(refund, cancellationToken);
        // Chờ Manager/Admin xác minh — chưa trừ ledger / PayOS / slot.
        await uow.SaveChangesAsync(cancellationToken);
        return ListMyRefundsQueryHandler.Map(refund);
    }

    internal static async Task CompleteRefundAsync(
        RefundRequest refund,
        IEventLedgerRepository ledgers,
        IPaymentFinancePort payments,
        IRegistrationCapacityPort capacity,
        IFinanceUnitOfWork uow,
        IPublisher publisher,
        CancellationToken cancellationToken)
    {
        refund.MarkProcessing();

        // PayOS mock/payout trước — fail → không persist refund/ledger (tránh kẹt Processing).
        await payments.MarkRefundedAsync(refund.PaymentId, refund.RefundAmount, cancellationToken);

        var ledger = await ledgers.GetByEventIdAsync(refund.EventId, cancellationToken);
        if (ledger is not null)
        {
            ledger.DebitRefund(refund.RefundAmount);
            ledgers.Update(ledger);
        }

        await capacity.ReleaseSlotAsync(refund.RegistrationId, cancellationToken);
        await capacity.MarkRegistrationCancelledAfterRefundAsync(refund.RegistrationId, cancellationToken);

        refund.MarkRefunded();
        await uow.SaveChangesAsync(cancellationToken);

        await publisher.Publish(
            new IntegrationEventOccurred(
                new RefundCompletedIntegrationEvent(
                    refund.Id,
                    refund.PaymentId,
                    refund.RegistrationId,
                    refund.EventId,
                    refund.UserId,
                    refund.RefundAmount)),
            cancellationToken);
    }
}

/// <summary>
/// BTC từ chối hồ sơ = hủy slot + hoàn theo chính sách ngày
/// (≥90d 100% / ≥60d 85% / &lt;60d hoặc đã xuất phát → 0%, vẫn Cancelled).
/// </summary>
public sealed class RegistrationRejectedRefundHandler(
    IRefundRequestRepository refunds,
    IEventLedgerRepository ledgers,
    IPlatformFinanceSettingsRepository settingsRepo,
    IPaymentFinancePort payments,
    IEventFinanceQuery events,
    IRegistrationCapacityPort capacity,
    IFinanceUnitOfWork uow,
    IPublisher publisher,
    ILogger<RegistrationRejectedRefundHandler> logger) : INotificationHandler<IntegrationEventOccurred>
{
    public async Task Handle(IntegrationEventOccurred notification, CancellationToken cancellationToken)
    {
        if (notification.Event is not RaceDay.Modules.Event.Contracts.Events.RegistrationRejectedIntegrationEvent rejected)
        {
            return;
        }

        try
        {
            var payment = await payments.GetPaidByRegistrationAsync(rejected.RegistrationId, cancellationToken);
            if (payment is null)
            {
                // Không Paid: có thể đã Refunded (retry) hoặc lệch dữ liệu — chỉ đảm bảo Cancelled, không ReleaseSlot lần 2.
                await capacity.MarkRegistrationCancelledAfterRefundAsync(rejected.RegistrationId, cancellationToken);
                logger.LogInformation(
                    "Reject {RegistrationId}: no Paid payment — cancelled registration without extra slot release.",
                    rejected.RegistrationId);
                return;
            }

            if (await refunds.HasOpenForPaymentAsync(payment.PaymentId, cancellationToken))
            {
                logger.LogWarning(
                    "Reject {RegistrationId}: open refund already exists for payment {PaymentId} — skip auto-refund.",
                    rejected.RegistrationId,
                    payment.PaymentId);
                return;
            }

            var ev = await events.GetEventAsync(rejected.RaceEventId, cancellationToken)
                ?? throw new DomainException("Không tìm thấy giải để hoàn sau reject.");

            var settings = await settingsRepo.GetAsync(cancellationToken);
            var (percent, amount) = LedgerCalculator.ComputeRefund(payment.Amount, settings, ev);

            if (amount <= 0)
            {
                await capacity.ReleaseSlotAsync(rejected.RegistrationId, cancellationToken);
                await capacity.MarkRegistrationCancelledAfterRefundAsync(rejected.RegistrationId, cancellationToken);
                logger.LogInformation(
                    "Reject {RegistrationId}: policy refund 0% (days={Days}) — released slot, cancelled, no payout.",
                    rejected.RegistrationId,
                    LedgerCalculator.DaysUntilEvent(ev));
                return;
            }

            var reason = string.IsNullOrWhiteSpace(rejected.Reason)
                ? "BTC từ chối hồ sơ — hoàn theo chính sách."
                : $"BTC từ chối hồ sơ: {rejected.Reason.Trim()}";

            var refund = RefundRequest.Create(
                payment.PaymentId,
                payment.RegistrationId,
                rejected.RaceEventId,
                payment.UserId,
                payment.Amount,
                amount,
                percent,
                reason,
                autoFromOrganizerReject: true);

            await refunds.AddAsync(refund, cancellationToken);
            await CreateRefundCommandHandler.CompleteRefundAsync(
                refund, ledgers, payments, capacity, uow, publisher, cancellationToken);

            logger.LogInformation(
                "Reject {RegistrationId}: auto-refund {Amount} ({Percent}%) payment {PaymentId}.",
                rejected.RegistrationId,
                amount,
                percent,
                payment.PaymentId);
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Failed auto-refund/cancel on reject registration {RegistrationId}.",
                rejected.RegistrationId);
            throw;
        }
    }
}

public sealed record ListSystemErrorRefundsQuery(bool PendingOnly = true)
    : IRequest<IReadOnlyList<ManagerSystemErrorRefundDto>>;

public sealed class ListSystemErrorRefundsQueryHandler(
    IRefundRequestRepository refunds,
    IEventFinanceQuery events,
    ICurrentUserAccessor currentUser)
    : IRequestHandler<ListSystemErrorRefundsQuery, IReadOnlyList<ManagerSystemErrorRefundDto>>
{
    public async Task<IReadOnlyList<ManagerSystemErrorRefundDto>> Handle(
        ListSystemErrorRefundsQuery request,
        CancellationToken cancellationToken)
    {
        EnsureManagerOrAdmin(currentUser);
        var list = await refunds.ListSystemErrorClaimsAsync(request.PendingOnly, cancellationToken);
        var result = new List<ManagerSystemErrorRefundDto>(list.Count);
        foreach (var r in list)
        {
            var reg = await events.GetRegistrationAsync(r.RegistrationId, cancellationToken);
            result.Add(ListMyRefundsQueryHandler.MapManager(r, reg));
        }

        return result;
    }

    internal static void EnsureManagerOrAdmin(ICurrentUserAccessor currentUser)
    {
        if (!currentUser.IsAdmin && !currentUser.IsManager)
        {
            throw new DomainException("Chỉ Manager hoặc Admin được duyệt khiếu nại lỗi hệ thống.");
        }
    }
}

public sealed record ApproveSystemErrorRefundCommand(Guid RefundId)
    : IRequest<ManagerSystemErrorRefundDto>;

public sealed class ApproveSystemErrorRefundCommandHandler(
    IRefundRequestRepository refunds,
    IEventLedgerRepository ledgers,
    IPaymentFinancePort payments,
    IRegistrationCapacityPort capacity,
    IEventFinanceQuery events,
    IFinanceUnitOfWork uow,
    IPublisher publisher,
    ICurrentUserAccessor currentUser)
    : IRequestHandler<ApproveSystemErrorRefundCommand, ManagerSystemErrorRefundDto>
{
    public async Task<ManagerSystemErrorRefundDto> Handle(
        ApproveSystemErrorRefundCommand request,
        CancellationToken cancellationToken)
    {
        ListSystemErrorRefundsQueryHandler.EnsureManagerOrAdmin(currentUser);
        var refund = await refunds.GetByIdAsync(request.RefundId, cancellationToken)
            ?? throw new DomainException("Không tìm thấy khiếu nại hoàn tiền.");
        refund.EnsureSystemErrorClaimPending();
        refunds.Update(refund);
        await CreateRefundCommandHandler.CompleteRefundAsync(
            refund, ledgers, payments, capacity, uow, publisher, cancellationToken);
        var reg = await events.GetRegistrationAsync(refund.RegistrationId, cancellationToken);
        return ListMyRefundsQueryHandler.MapManager(refund, reg);
    }
}

public sealed record RejectSystemErrorRefundCommand(Guid RefundId, string Reason)
    : IRequest<ManagerSystemErrorRefundDto>;

public sealed class RejectSystemErrorRefundCommandValidator : AbstractValidator<RejectSystemErrorRefundCommand>
{
    public RejectSystemErrorRefundCommandValidator()
        => RuleFor(x => x.Reason).NotEmpty().MaximumLength(1000);
}

public sealed class RejectSystemErrorRefundCommandHandler(
    IRefundRequestRepository refunds,
    IEventFinanceQuery events,
    IFinanceUnitOfWork uow,
    ICurrentUserAccessor currentUser)
    : IRequestHandler<RejectSystemErrorRefundCommand, ManagerSystemErrorRefundDto>
{
    public async Task<ManagerSystemErrorRefundDto> Handle(
        RejectSystemErrorRefundCommand request,
        CancellationToken cancellationToken)
    {
        ListSystemErrorRefundsQueryHandler.EnsureManagerOrAdmin(currentUser);
        var refund = await refunds.GetByIdAsync(request.RefundId, cancellationToken)
            ?? throw new DomainException("Không tìm thấy khiếu nại hoàn tiền.");
        refund.EnsureSystemErrorClaimPending();
        refund.Reject(request.Reason);
        refunds.Update(refund);
        await uow.SaveChangesAsync(cancellationToken);
        var reg = await events.GetRegistrationAsync(refund.RegistrationId, cancellationToken);
        return ListMyRefundsQueryHandler.MapManager(refund, reg);
    }
}
