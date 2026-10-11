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
        EnsureAdmin(currentUser);
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
}

public sealed record ApproveAdminPayoutCommand(Guid PayoutId, string? ReceiptUrl) : IRequest<AdminPayoutRequestDto>;

public sealed class ApproveAdminPayoutCommandHandler(
    IPayoutRequestRepository payouts,
    IEventLedgerRepository ledgers,
    IEventFinanceQuery events,
    IFinanceUnitOfWork uow,
    ICurrentUserAccessor currentUser) : IRequestHandler<ApproveAdminPayoutCommand, AdminPayoutRequestDto>
{
    public async Task<AdminPayoutRequestDto> Handle(ApproveAdminPayoutCommand request, CancellationToken cancellationToken)
    {
        ListAdminPayoutsQueryHandler.EnsureAdmin(currentUser);
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
        return new AdminPayoutRequestDto(
            p.Id, p.OrganizerUserId, ev?.AccountHolder ?? p.AccountHolderName,
            p.EventId, p.EventName, p.Amount, p.Note,
            p.Status.ToString().ToLowerInvariant(), p.RejectReason,
            new BankAccountDto(p.BankName, p.AccountNumber, p.AccountHolderName, p.Branch, p.TransferNote),
            p.CreatedAtUtc, p.ReceiptUrl);
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
        ListAdminPayoutsQueryHandler.EnsureAdmin(currentUser);
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
        return new AdminPayoutRequestDto(
            p.Id, p.OrganizerUserId, ev?.AccountHolder ?? p.AccountHolderName,
            p.EventId, p.EventName, p.Amount, p.Note,
            p.Status.ToString().ToLowerInvariant(), p.RejectReason,
            new BankAccountDto(p.BankName, p.AccountNumber, p.AccountHolderName, p.Branch, p.TransferNote),
            p.CreatedAtUtc, p.ReceiptUrl);
    }
}

public sealed record GetPlatformOverviewQuery : IRequest<PlatformFinanceOverviewDto>;

public sealed class GetPlatformOverviewQueryHandler(
    IEventLedgerRepository ledgers,
    IPayoutRequestRepository payouts,
    IPlatformFinanceSettingsRepository settingsRepo,
    ICurrentUserAccessor currentUser) : IRequestHandler<GetPlatformOverviewQuery, PlatformFinanceOverviewDto>
{
    public async Task<PlatformFinanceOverviewDto> Handle(GetPlatformOverviewQuery request, CancellationToken cancellationToken)
    {
        ListAdminPayoutsQueryHandler.EnsureAdmin(currentUser);
        var settings = await settingsRepo.GetAsync(cancellationToken);
        var all = await ledgers.ListAllAsync(cancellationToken);
        var fee = all.Sum(l => l.PlatformFeeAmount(settings.PlatformFeePercent));
        var escrow = all.Sum(l => l.EscrowHeld(settings.PlatformFeePercent));
        var pending = all.Sum(l => l.PayoutPending);
        var monthStart = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var transferred = (await payouts.ListAllAsync(cancellationToken))
            .Where(p => p.Status == PayoutRequestStatus.Transferred && p.TransferredAtUtc >= monthStart)
            .Sum(p => p.Amount);

        return new PlatformFinanceOverviewDto(escrow, fee, pending, transferred, "VND");
    }
}

public sealed record GetPlatformSettingsQuery : IRequest<PlatformFeeSettingsDto>;
public sealed record SavePlatformSettingsCommand(PlatformFeeSettingsDto Body) : IRequest<PlatformFeeSettingsDto>;

public sealed class GetPlatformSettingsQueryHandler(
    IPlatformFinanceSettingsRepository settingsRepo,
    ICurrentUserAccessor currentUser) : IRequestHandler<GetPlatformSettingsQuery, PlatformFeeSettingsDto>
{
    public async Task<PlatformFeeSettingsDto> Handle(GetPlatformSettingsQuery request, CancellationToken cancellationToken)
    {
        ListAdminPayoutsQueryHandler.EnsureAdmin(currentUser);
        var s = await settingsRepo.GetAsync(cancellationToken);
        return Map(s);
    }

    internal static PlatformFeeSettingsDto Map(Domain.Settings.PlatformFinanceSettings s) => new(
        s.PlatformFeePercent,
        s.PreEventWithdrawPercent,
        s.PostEventWithdrawPercent,
        s.MinPayoutAmount,
        s.Notes,
        s.FullRefundPercent,
        s.PartialRefundPercent,
        s.AllowRefundAfterEventStart);
}

public sealed class SavePlatformSettingsCommandHandler(
    IPlatformFinanceSettingsRepository settingsRepo,
    IFinanceUnitOfWork uow,
    ICurrentUserAccessor currentUser) : IRequestHandler<SavePlatformSettingsCommand, PlatformFeeSettingsDto>
{
    public async Task<PlatformFeeSettingsDto> Handle(SavePlatformSettingsCommand request, CancellationToken cancellationToken)
    {
        ListAdminPayoutsQueryHandler.EnsureAdmin(currentUser);
        var s = await settingsRepo.GetAsync(cancellationToken);
        var b = request.Body;
        s.Update(
            b.PlatformFeePercent,
            b.PreEventWithdrawPercent,
            b.PostEventWithdrawPercent,
            b.MinPayoutAmount,
            b.FullRefundPercent,
            b.PartialRefundPercent,
            b.AllowRefundAfterEventStart,
            b.Notes);
        settingsRepo.Update(s);
        await uow.SaveChangesAsync(cancellationToken);
        return GetPlatformSettingsQueryHandler.Map(s);
    }
}
