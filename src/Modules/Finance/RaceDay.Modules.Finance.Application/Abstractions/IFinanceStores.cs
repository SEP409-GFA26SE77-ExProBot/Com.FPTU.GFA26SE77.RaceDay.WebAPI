using RaceDay.Modules.Finance.Domain.Ledger;
using RaceDay.Modules.Finance.Domain.Payouts;
using RaceDay.Modules.Finance.Domain.Refunds;
using RaceDay.Modules.Finance.Domain.Settings;

namespace RaceDay.Modules.Finance.Application.Abstractions;

public interface IFinanceUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}

public interface IEventLedgerRepository
{
    Task<EventLedger?> GetByEventIdAsync(Guid eventId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<EventLedger>> ListByOrganizerAsync(Guid organizerUserId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<EventLedger>> ListAllAsync(CancellationToken cancellationToken = default);
    Task AddAsync(EventLedger ledger, CancellationToken cancellationToken = default);
    void Update(EventLedger ledger);
}

public interface IPayoutRequestRepository
{
    Task<PayoutRequest?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<PayoutRequest>> ListByOrganizerAsync(Guid organizerUserId, Guid? eventId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<PayoutRequest>> ListPendingAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<PayoutRequest>> ListAllAsync(CancellationToken cancellationToken = default);
    Task AddAsync(PayoutRequest request, CancellationToken cancellationToken = default);
    void Update(PayoutRequest request);
}

public interface IRefundRequestRepository
{
    Task<RefundRequest?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<bool> HasOpenForPaymentAsync(Guid paymentId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<RefundRequest>> ListByUserAsync(Guid userId, CancellationToken cancellationToken = default);
    Task AddAsync(RefundRequest request, CancellationToken cancellationToken = default);
    void Update(RefundRequest request);
}

public interface IOrganizerBankAccountRepository
{
    Task<OrganizerBankAccount?> GetByUserAsync(Guid organizerUserId, CancellationToken cancellationToken = default);
    Task AddAsync(OrganizerBankAccount account, CancellationToken cancellationToken = default);
    void Update(OrganizerBankAccount account);
}

public interface IPlatformFinanceSettingsRepository
{
    Task<PlatformFinanceSettings> GetAsync(CancellationToken cancellationToken = default);
    void Update(PlatformFinanceSettings settings);
}
