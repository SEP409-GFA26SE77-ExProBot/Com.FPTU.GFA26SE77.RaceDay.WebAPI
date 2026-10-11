using FluentValidation;
using MediatR;
using RaceDay.Modules.Event.Contracts.Finance;
using RaceDay.Modules.Finance.Application.Abstractions;
using RaceDay.Modules.Finance.Application.Wallet;
using RaceDay.Modules.Finance.Domain.Payouts;
using RaceDay.Shared.Application.Abstractions;
using RaceDay.Shared.Domain.Exceptions;

namespace RaceDay.Modules.Finance.Application.Payouts;

public sealed record ListPayoutRequestsQuery(Guid? EventId) : IRequest<IReadOnlyList<PayoutRequestDto>>;

public sealed class ListPayoutRequestsQueryHandler(
    IPayoutRequestRepository payouts,
    ICurrentUserAccessor currentUser) : IRequestHandler<ListPayoutRequestsQuery, IReadOnlyList<PayoutRequestDto>>
{
    public async Task<IReadOnlyList<PayoutRequestDto>> Handle(ListPayoutRequestsQuery request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new DomainException("Cần đăng nhập.");
        var list = await payouts.ListByOrganizerAsync(userId, request.EventId, cancellationToken);
        return list.Select(Map).ToList();
    }

    internal static PayoutRequestDto Map(PayoutRequest p) => new(
        p.Id,
        p.EventId,
        p.EventName,
        p.Amount,
        p.Note,
        p.Status.ToString().ToLowerInvariant(),
        p.RejectReason,
        new BankAccountDto(p.BankName, p.AccountNumber, p.AccountHolderName, p.Branch, p.TransferNote),
        p.CreatedAtUtc,
        p.UpdatedAtUtc,
        p.TransferredAtUtc,
        p.ReceiptUrl);
}

public sealed record CreatePayoutCommand(CreatePayoutRequestDto Body) : IRequest<PayoutRequestDto>;

public sealed class CreatePayoutCommandValidator : AbstractValidator<CreatePayoutCommand>
{
    public CreatePayoutCommandValidator()
    {
        RuleFor(x => x.Body.EventId).NotEmpty();
        RuleFor(x => x.Body.Amount).GreaterThan(0);
    }
}

public sealed class CreatePayoutCommandHandler(
    IEventLedgerRepository ledgers,
    IPayoutRequestRepository payouts,
    IOrganizerBankAccountRepository banks,
    IPlatformFinanceSettingsRepository settingsRepo,
    IEventFinanceQuery events,
    IFinanceUnitOfWork uow,
    ICurrentUserAccessor currentUser) : IRequestHandler<CreatePayoutCommand, PayoutRequestDto>
{
    public async Task<PayoutRequestDto> Handle(CreatePayoutCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new DomainException("Cần đăng nhập.");
        var body = request.Body;
        var ev = await events.GetEventAsync(body.EventId, cancellationToken)
            ?? throw new DomainException("Không tìm thấy giải.");
        if (ev.OrganizerUserId != userId && !currentUser.IsAdmin)
        {
            throw new DomainException("Bạn không phải chủ giải này.");
        }

        var settings = await settingsRepo.GetAsync(cancellationToken);
        if (body.Amount < settings.MinPayoutAmount)
        {
            throw new DomainException($"Số tiền rút tối thiểu {settings.MinPayoutAmount:N0}₫.");
        }

        var bank = await banks.GetByUserAsync(userId, cancellationToken);
        // Fallback: event beneficiary from create-event wizard
        string bankName, accountNumber, holder, branch, note;
        if (bank is not null)
        {
            bankName = bank.BankName;
            accountNumber = bank.AccountNumber;
            holder = bank.AccountHolderName;
            branch = bank.Branch ?? "";
            note = bank.TransferNote ?? "";
        }
        else if (!string.IsNullOrWhiteSpace(ev.AccountNumber) && !string.IsNullOrWhiteSpace(ev.BankName))
        {
            bankName = ev.BankName!;
            accountNumber = ev.AccountNumber!;
            holder = ev.AccountHolder ?? ev.Name;
            branch = ev.BankBranch ?? "";
            note = ev.TransferNote ?? "";
        }
        else
        {
            throw new DomainException("Cần lưu tài khoản ngân hàng trước khi rút.");
        }

        var ledger = await ledgers.GetByEventIdAsync(body.EventId, cancellationToken)
            ?? throw new DomainException("Không tìm thấy túi tiền của giải (chưa có vé paid).");
        var dto = LedgerCalculator.ToDto(ledger, settings, ev);
        if (body.Amount > dto.AvailableToWithdraw)
        {
            throw new DomainException(
                $"Vượt khả dụng của giải này ({dto.AvailableToWithdraw:N0}₫). Không lấy tiền từ giải khác.");
        }

        ledger.ReservePayout(body.Amount);
        ledgers.Update(ledger);

        var payout = PayoutRequest.Create(
            body.EventId,
            userId,
            ev.Name,
            body.Amount,
            body.Note,
            bankName,
            accountNumber,
            holder,
            string.IsNullOrWhiteSpace(branch) ? null : branch,
            string.IsNullOrWhiteSpace(note) ? null : note);
        await payouts.AddAsync(payout, cancellationToken);
        await uow.SaveChangesAsync(cancellationToken);
        return ListPayoutRequestsQueryHandler.Map(payout);
    }
}
