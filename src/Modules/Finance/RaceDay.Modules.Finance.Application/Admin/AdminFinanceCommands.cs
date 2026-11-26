using FluentValidation;
using MediatR;
using RaceDay.Modules.Event.Contracts.Finance;
using RaceDay.Modules.Finance.Application.Abstractions;
using RaceDay.Modules.Finance.Application.Payouts;
using RaceDay.Modules.Finance.Application.Wallet;
using RaceDay.Modules.Finance.Domain.Payouts;
using RaceDay.Shared.Application.Abstractions;
using RaceDay.Shared.Domain.Exceptions;

namespace RaceDay.Modules.Finance.Application.Admin;

public sealed record ListAdminPayoutsQuery : IRequest<IReadOnlyList<AdminPayoutRequestDto>>;

public sealed class ListAdminPayoutsQueryHandler(
    IPayoutRequestRepository payouts,
    IEventFinanceQuery events,
    ICurrentUserAccessor currentUser) : IRequestHandler<ListAdminPayoutsQuery, IReadOnlyList<AdminPayoutRequestDto>>
{
    public async Task<IReadOnlyList<AdminPayoutRequestDto>> Handle(ListAdminPayoutsQuery request, CancellationToken cancellationToken)
    {
        EnsureManagerOrAdmin(currentUser);
        var list = await payouts.ListAllAsync(cancellationToken);
        var result = new List<AdminPayoutRequestDto>();
        foreach (var p in list.OrderByDescending(x => x.CreatedAtUtc))
        {
            var ev = await events.GetEventAsync(p.EventId, cancellationToken);
            result.Add(new AdminPayoutRequestDto(
                p.Id,
                p.OrganizerUserId,
                ev?.AccountHolder ?? p.AccountHolderName,
                p.EventId,
                p.EventName,
                p.Amount,
                p.Note,
                p.Status.ToString().ToLowerInvariant(),
                p.RejectReason,
                new BankAccountDto(p.BankName, p.AccountNumber, p.AccountHolderName, p.Branch, p.TransferNote),
                p.CreatedAtUtc,
                p.ReceiptUrl));
        }

        return result;
    }

    internal static void EnsureAdmin(ICurrentUserAccessor currentUser)
    {
        if (!currentUser.IsAdmin)
        {
            throw new DomainException("Chỉ Admin.");
        }
    }

    internal static void EnsureManagerOrAdmin(ICurrentUserAccessor currentUser)
    {
        if (!currentUser.IsAdmin && !currentUser.IsManager)
        {
            throw new DomainException("Chỉ Manager hoặc Admin được duyệt giải ngân.");
        }
    }
}

public sealed record ApproveAdminPayoutCommand(Guid PayoutId) : IRequest<AdminPayoutRequestDto>;

public sealed class ApproveAdminPayoutCommandHandler(
    IPayoutRequestRepository payouts,
    IEventFinanceQuery events,
    IFinanceUnitOfWork uow,
    ICurrentUserAccessor currentUser) : IRequestHandler<ApproveAdminPayoutCommand, AdminPayoutRequestDto>
{
    public async Task<AdminPayoutRequestDto> Handle(ApproveAdminPayoutCommand request, CancellationToken cancellationToken)
    {
        ListAdminPayoutsQueryHandler.EnsureManagerOrAdmin(currentUser);
        var p = await payouts.GetByIdAsync(request.PayoutId, cancellationToken)
            ?? throw new DomainException("Không tìm thấy yêu cầu.");
        p.Approve();
        payouts.Update(p);
        await uow.SaveChangesAsync(cancellationToken);
        var ev = await events.GetEventAsync(p.EventId, cancellationToken);
        return MapAdmin(p, ev);
    }

    internal static AdminPayoutRequestDto MapAdmin(PayoutRequest p, EventFinanceInfo? ev) => new(
        p.Id, p.OrganizerUserId, ev?.AccountHolder ?? p.AccountHolderName,
        p.EventId, p.EventName, p.Amount, p.Note,
        p.Status.ToString().ToLowerInvariant(), p.RejectReason,
        new BankAccountDto(p.BankName, p.AccountNumber, p.AccountHolderName, p.Branch, p.TransferNote),
        p.CreatedAtUtc, p.ReceiptUrl);
}

/// <summary>Bước 2: xác nhận đã CK ngoài hệ thống + biên nhận bắt buộc → cập nhật ledger.</summary>
public sealed record ConfirmTransferAdminPayoutCommand(Guid PayoutId, string ReceiptUrl) : IRequest<AdminPayoutRequestDto>;

public sealed class ConfirmTransferAdminPayoutCommandValidator : AbstractValidator<ConfirmTransferAdminPayoutCommand>
{
    public ConfirmTransferAdminPayoutCommandValidator()
        => RuleFor(x => x.ReceiptUrl).NotEmpty().MaximumLength(2000);
}

public sealed class ConfirmTransferAdminPayoutCommandHandler(
    IPayoutRequestRepository payouts,
    IEventLedgerRepository ledgers,
    IEventFinanceQuery events,
    IFinanceUnitOfWork uow,
    ICurrentUserAccessor currentUser) : IRequestHandler<ConfirmTransferAdminPayoutCommand, AdminPayoutRequestDto>
{
    public async Task<AdminPayoutRequestDto> Handle(ConfirmTransferAdminPayoutCommand request, CancellationToken cancellationToken)
    {
        ListAdminPayoutsQueryHandler.EnsureManagerOrAdmin(currentUser);
        var p = await payouts.GetByIdAsync(request.PayoutId, cancellationToken)
            ?? throw new DomainException("Không tìm thấy yêu cầu.");
        p.MarkTransferred(request.ReceiptUrl);
        payouts.Update(p);

        var ledger = await ledgers.GetByEventIdAsync(p.EventId, cancellationToken);
        if (ledger is not null)
        {
            ledger.CompletePayout(p.Amount);
            ledgers.Update(ledger);
        }

        await uow.SaveChangesAsync(cancellationToken);
        var ev = await events.GetEventAsync(p.EventId, cancellationToken);
        return ApproveAdminPayoutCommandHandler.MapAdmin(p, ev);
    }
}

public sealed record RejectAdminPayoutCommand(Guid PayoutId, string Reason) : IRequest<AdminPayoutRequestDto>;

public sealed class RejectAdminPayoutCommandValidator : AbstractValidator<RejectAdminPayoutCommand>
{
    public RejectAdminPayoutCommandValidator() => RuleFor(x => x.Reason).NotEmpty().MaximumLength(1000);
}

public sealed class RejectAdminPayoutCommandHandler(
    IPayoutRequestRepository payouts,
    IEventLedgerRepository ledgers,
    IEventFinanceQuery events,
    IFinanceUnitOfWork uow,
    ICurrentUserAccessor currentUser) : IRequestHandler<RejectAdminPayoutCommand, AdminPayoutRequestDto>
{
    public async Task<AdminPayoutRequestDto> Handle(RejectAdminPayoutCommand request, CancellationToken cancellationToken)
    {
        ListAdminPayoutsQueryHandler.EnsureManagerOrAdmin(currentUser);
        var p = await payouts.GetByIdAsync(request.PayoutId, cancellationToken)
            ?? throw new DomainException("Không tìm thấy yêu cầu.");
        p.Reject(request.Reason);
        payouts.Update(p);

        var ledger = await ledgers.GetByEventIdAsync(p.EventId, cancellationToken);
        if (ledger is not null)
        {
            ledger.ReleasePayoutReserve(p.Amount);
            ledgers.Update(ledger);
        }

        await uow.SaveChangesAsync(cancellationToken);
        var ev = await events.GetEventAsync(p.EventId, cancellationToken);
        return ApproveAdminPayoutCommandHandler.MapAdmin(p, ev);
    }
}

