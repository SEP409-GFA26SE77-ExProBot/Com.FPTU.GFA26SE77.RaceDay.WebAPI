using FluentValidation;
using MediatR;
using Microsoft.Extensions.Logging;
using RaceDay.Modules.Event.Contracts.Finance;
using RaceDay.Modules.Finance.Application.Abstractions;
using RaceDay.Modules.Finance.Application.Wallet;
using RaceDay.Modules.Finance.Contracts.Events;
using RaceDay.Modules.Finance.Domain.Refunds;
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
        r.RejectReason);
}

public sealed record CreateRefundCommand(CreateRefundRequestDto Body) : IRequest<RefundRequestDto>;

public sealed class CreateRefundCommandValidator : AbstractValidator<CreateRefundCommand>
{
    public CreateRefundCommandValidator()
    {
        RuleFor(x => x.Body.PaymentId).NotEmpty();
        RuleFor(x => x.Body.Reason).NotEmpty().MaximumLength(1000);
    }
}

public sealed class CreateRefundCommandHandler(
    IRefundRequestRepository refunds,
    IEventLedgerRepository ledgers,
    IPlatformFinanceSettingsRepository settingsRepo,
    IPaymentFinancePort payments,
    IEventFinanceQuery events,
    IRegistrationCapacityPort capacity,
    IFinanceUnitOfWork uow,
    IPublisher publisher,
    ICurrentUserAccessor currentUser) : IRequestHandler<CreateRefundCommand, RefundRequestDto>
{
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
        var ev = await events.GetEventAsync(reg.EventId, cancellationToken)
            ?? throw new DomainException("Không tìm thấy giải.");

        var settings = await settingsRepo.GetAsync(cancellationToken);
        var (percent, computed) = LedgerCalculator.ComputeRefund(payment.Amount, settings, ev);
        if (computed <= 0)
        {
            throw new DomainException(
                settings.AllowRefundAfterEventStart
                    ? "Không đủ điều kiện hoàn tiền theo chính sách hiện tại."
                    : "Đã qua giờ xuất phát — không hoàn tiền VĐV (trừ khi Admin/organizer từ chối hồ sơ).");
        }

        // Amount always from paid snapshot × policy % — ignore FE garbage amount; allow lower optional request.
        var amount = computed;
        if (request.Body.Amount is decimal asked && asked > 0)
        {
            amount = Math.Min(asked, computed);
        }

        var refund = RefundRequest.Create(
            payment.PaymentId,
            payment.RegistrationId,
            reg.EventId,
            payment.UserId,
            payment.Amount,
            amount,
            percent,
            request.Body.Reason);

        await refunds.AddAsync(refund, cancellationToken);
        await CompleteRefundAsync(refund, ledgers, payments, capacity, uow, publisher, cancellationToken);
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
/// Trước đây: reject → auto hoàn tiền. Đổi policy: giữ tiền, VĐV sửa hồ sơ và nộp lại.
/// Hoàn thủ công (nếu BTC muốn) qua luồng refund riêng.
/// </summary>
public sealed class RegistrationRejectedRefundHandler(
    ILogger<RegistrationRejectedRefundHandler> logger) : INotificationHandler<IntegrationEventOccurred>
{
    public Task Handle(IntegrationEventOccurred notification, CancellationToken cancellationToken)
    {
        if (notification.Event is RaceDay.Modules.Event.Contracts.Events.RegistrationRejectedIntegrationEvent rejected)
        {
            logger.LogInformation(
                "Skip auto-refund on reject registration {RegistrationId} — runner may edit & resubmit. Reason: {Reason}",
                rejected.RegistrationId,
                rejected.Reason);
        }

        return Task.CompletedTask;
    }
}
