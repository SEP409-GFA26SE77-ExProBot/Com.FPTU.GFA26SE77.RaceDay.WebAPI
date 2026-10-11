using FluentValidation;
using MediatR;
using RaceDay.Modules.Event.Contracts.Finance;
using RaceDay.Modules.Finance.Application.Abstractions;
using RaceDay.Modules.Finance.Domain.Settings;
using RaceDay.Shared.Application.Abstractions;
using RaceDay.Shared.Domain.Exceptions;

namespace RaceDay.Modules.Finance.Application.Wallet;

public sealed record GetOrganizerWalletQuery : IRequest<OrganizerWalletDto>;

public sealed class GetOrganizerWalletQueryHandler(
    IEventLedgerRepository ledgers,
    IOrganizerBankAccountRepository banks,
    IPlatformFinanceSettingsRepository settingsRepo,
    IEventFinanceQuery events,
    ICurrentUserAccessor currentUser) : IRequestHandler<GetOrganizerWalletQuery, OrganizerWalletDto>
{
    public async Task<OrganizerWalletDto> Handle(GetOrganizerWalletQuery request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new DomainException("Cần đăng nhập.");
        var settings = await settingsRepo.GetAsync(cancellationToken);
        var list = await ledgers.ListByOrganizerAsync(userId, cancellationToken);
        var eventInfos = await events.ListByOrganizerAsync(userId, cancellationToken);
        var byId = eventInfos.ToDictionary(x => x.EventId);

        // Ensure empty ledgers appear for organizer events with no payments yet
        var dtos = new List<EventLedgerDto>();
        foreach (var ev in eventInfos)
        {
            var ledger = list.FirstOrDefault(l => l.EventId == ev.EventId)
                ?? Domain.Ledger.EventLedger.Create(ev.EventId, userId, ev.Name, "VND");
            dtos.Add(LedgerCalculator.ToDto(ledger, settings, byId.GetValueOrDefault(ev.EventId)));
        }

        // Also include ledgers whose event query missed (edge)
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
        var ledger = await ledgers.GetByEventIdAsync(request.EventId, cancellationToken)
            ?? Domain.Ledger.EventLedger.Create(ev.EventId, ev.OrganizerUserId, ev.Name, "VND");
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
