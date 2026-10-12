using FluentValidation;
using MediatR;
using RaceDay.Modules.Event.Contracts.Finance;
using RaceDay.Modules.Finance.Application.Abstractions;
using RaceDay.Modules.Finance.Domain.Settings;
using RaceDay.Modules.Payment.Contracts.Payments;
using RaceDay.Shared.Application.Abstractions;
using RaceDay.Shared.Domain.Exceptions;

namespace RaceDay.Modules.Finance.Application.Wallet;

public sealed record GetOrganizerWalletQuery : IRequest<OrganizerWalletDto>;

public sealed class GetOrganizerWalletQueryHandler(
    IEventLedgerRepository ledgers,
    IOrganizerBankAccountRepository banks,
    IPlatformFinanceSettingsRepository settingsRepo,
    IEventFinanceQuery events,
    IPaymentFinancePort payments,
    IFinanceUnitOfWork uow,
    Microsoft.Extensions.Logging.ILogger<GetOrganizerWalletQueryHandler> logger,
    ICurrentUserAccessor currentUser) : IRequestHandler<GetOrganizerWalletQuery, OrganizerWalletDto>
{
    public async Task<OrganizerWalletDto> Handle(GetOrganizerWalletQuery request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new DomainException("Cần đăng nhập.");
        var settings = await settingsRepo.GetAsync(cancellationToken);
        var eventInfos = await events.ListByOrganizerAsync(userId, cancellationToken);
        var byId = eventInfos.ToDictionary(x => x.EventId);

        var dtos = new List<EventLedgerDto>();
        foreach (var ev in eventInfos)
        {
            var ledger = await EventLedgerReconciler.ReconcileAsync(
                ev.EventId, ev, ledgers, events, payments, uow, logger, cancellationToken);
            dtos.Add(LedgerCalculator.ToDto(ledger, settings, byId.GetValueOrDefault(ev.EventId)));
        }

        var list = await ledgers.ListByOrganizerAsync(userId, cancellationToken);
        foreach (var ledger in list.Where(l => dtos.All(d => d.EventId != l.EventId)))
        {
            dtos.Add(LedgerCalculator.ToDto(ledger, settings, byId.GetValueOrDefault(ledger.EventId)));
        }

        var bank = await banks.GetByUserAsync(userId, cancellationToken);
        return new OrganizerWalletDto(
            dtos.Sum(x => x.GrossRevenue),
            dtos.Sum(x => x.AvailableToWithdraw),
            dtos.Sum(x => x.EscrowHeld + x.PayoutPending),
            dtos.Sum(x => x.WithdrawnTotal),
            "VND",
            bank is null ? null : new BankAccountDto(
                bank.BankName, bank.AccountNumber, bank.AccountHolderName, bank.Branch, bank.TransferNote, bank.Bin),
            dtos);
    }
}

public sealed record ListEventLedgersQuery : IRequest<IReadOnlyList<EventLedgerDto>>;

public sealed class ListEventLedgersQueryHandler(IMediator mediator)
    : IRequestHandler<ListEventLedgersQuery, IReadOnlyList<EventLedgerDto>>
{
    public async Task<IReadOnlyList<EventLedgerDto>> Handle(ListEventLedgersQuery request, CancellationToken cancellationToken)
    {
        var wallet = await mediator.Send(new GetOrganizerWalletQuery(), cancellationToken);
        return wallet.Events;
    }
}

public sealed record GetEventFinanceSummaryQuery(Guid EventId) : IRequest<EventLedgerDto>;

public sealed class GetEventFinanceSummaryQueryHandler(
    IEventLedgerRepository ledgers,
    IPlatformFinanceSettingsRepository settingsRepo,
    IEventFinanceQuery events,
    IPaymentFinancePort payments,
    IFinanceUnitOfWork uow,
    Microsoft.Extensions.Logging.ILogger<GetEventFinanceSummaryQueryHandler> logger,
    ICurrentUserAccessor currentUser) : IRequestHandler<GetEventFinanceSummaryQuery, EventLedgerDto>
{
    public async Task<EventLedgerDto> Handle(GetEventFinanceSummaryQuery request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new DomainException("Cần đăng nhập.");
        var ev = await events.GetEventAsync(request.EventId, cancellationToken)
            ?? throw new DomainException("Không tìm thấy giải.");
        if (!currentUser.IsAdmin && ev.OrganizerUserId != userId)
        {
            throw new DomainException("Bạn không phải chủ giải này.");
        }

        var settings = await settingsRepo.GetAsync(cancellationToken);
        // Auto-heal: nếu ledger trống/miss trong khi đã có payment Paid → sync từ payment.
        var ledger = await EventLedgerReconciler.ReconcileAsync(
            request.EventId, ev, ledgers, events, payments, uow, logger, cancellationToken);
        return LedgerCalculator.ToDto(ledger, settings, ev);
    }
}

public sealed record SaveOrganizerBankAccountCommand(BankAccountDto Account) : IRequest<OrganizerWalletDto>;

public sealed class SaveOrganizerBankAccountCommandValidator : AbstractValidator<SaveOrganizerBankAccountCommand>
{
    public SaveOrganizerBankAccountCommandValidator()
    {
        RuleFor(x => x.Account.BankName).NotEmpty().MaximumLength(120);
        RuleFor(x => x.Account.AccountNumber).NotEmpty().MaximumLength(50);
        RuleFor(x => x.Account.AccountHolderName).NotEmpty().MaximumLength(120);
    }
}

public sealed class SaveOrganizerBankAccountCommandHandler(
    IOrganizerBankAccountRepository banks,
    IFinanceUnitOfWork uow,
    ICurrentUserAccessor currentUser,
    IMediator mediator) : IRequestHandler<SaveOrganizerBankAccountCommand, OrganizerWalletDto>
{
    public async Task<OrganizerWalletDto> Handle(SaveOrganizerBankAccountCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new DomainException("Cần đăng nhập.");
        var existing = await banks.GetByUserAsync(userId, cancellationToken);
        if (existing is null)
        {
            await banks.AddAsync(OrganizerBankAccount.Create(
                userId,
                request.Account.BankName,
                request.Account.AccountNumber,
                request.Account.AccountHolderName,
                request.Account.Branch,
                request.Account.TransferNote,
                request.Account.Bin), cancellationToken);
        }
        else
        {
            existing.Replace(
                request.Account.BankName,
                request.Account.AccountNumber,
                request.Account.AccountHolderName,
                request.Account.Branch,
                request.Account.TransferNote,
                request.Account.Bin);
            banks.Update(existing);
        }

        await uow.SaveChangesAsync(cancellationToken);
        return await mediator.Send(new GetOrganizerWalletQuery(), cancellationToken);
    }
}
