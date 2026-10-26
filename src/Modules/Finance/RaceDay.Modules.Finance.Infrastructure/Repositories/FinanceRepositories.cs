using Microsoft.EntityFrameworkCore;
using RaceDay.Modules.Finance.Application.Abstractions;
using RaceDay.Modules.Finance.Domain.Ledger;
using RaceDay.Modules.Finance.Domain.Payouts;
using RaceDay.Modules.Finance.Domain.Refunds;
using RaceDay.Modules.Finance.Domain.Settings;
using RaceDay.Modules.Finance.Infrastructure.Persistence;

namespace RaceDay.Modules.Finance.Infrastructure.Repositories;

public sealed class FinanceUnitOfWork(FinanceDbContext db) : IFinanceUnitOfWork
{
    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        => db.SaveChangesAsync(cancellationToken);
}

public sealed class EventLedgerRepository(FinanceDbContext db) : IEventLedgerRepository
{
    public Task<EventLedger?> GetByEventIdAsync(Guid eventId, CancellationToken cancellationToken = default)
        => db.EventLedgers.FirstOrDefaultAsync(x => x.EventId == eventId, cancellationToken);

    public async Task<IReadOnlyList<EventLedger>> ListByOrganizerAsync(Guid organizerUserId, CancellationToken cancellationToken = default)
        => await db.EventLedgers.Where(x => x.OrganizerUserId == organizerUserId).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<EventLedger>> ListAllAsync(CancellationToken cancellationToken = default)
        => await db.EventLedgers.ToListAsync(cancellationToken);

    public async Task AddAsync(EventLedger ledger, CancellationToken cancellationToken = default)
        => await db.EventLedgers.AddAsync(ledger, cancellationToken);

    public void Update(EventLedger ledger) => db.EventLedgers.Update(ledger);
}

public sealed class PayoutRequestRepository(FinanceDbContext db) : IPayoutRequestRepository
{
    public Task<PayoutRequest?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        => db.PayoutRequests.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

    public async Task<IReadOnlyList<PayoutRequest>> ListByOrganizerAsync(
        Guid organizerUserId, Guid? eventId, CancellationToken cancellationToken = default)
    {
        var q = db.PayoutRequests.Where(x => x.OrganizerUserId == organizerUserId);
        if (eventId is Guid eid)
        {
            q = q.Where(x => x.EventId == eid);
        }

        return await q.OrderByDescending(x => x.CreatedAtUtc).ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<PayoutRequest>> ListPendingAsync(CancellationToken cancellationToken = default)
        => await db.PayoutRequests.Where(x => x.Status == PayoutRequestStatus.Pending)
            .OrderByDescending(x => x.CreatedAtUtc).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<PayoutRequest>> ListAllAsync(CancellationToken cancellationToken = default)
        => await db.PayoutRequests.OrderByDescending(x => x.CreatedAtUtc).ToListAsync(cancellationToken);

    public async Task AddAsync(PayoutRequest request, CancellationToken cancellationToken = default)
        => await db.PayoutRequests.AddAsync(request, cancellationToken);

    public void Update(PayoutRequest request) => db.PayoutRequests.Update(request);
}

public sealed class RefundRequestRepository(FinanceDbContext db) : IRefundRequestRepository
{
    public Task<RefundRequest?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        => db.RefundRequests.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

    public Task<bool> HasOpenForPaymentAsync(Guid paymentId, CancellationToken cancellationToken = default)
        => db.RefundRequests.AnyAsync(
            x => x.PaymentId == paymentId && x.Status != RefundRequestStatus.Rejected,
            cancellationToken);

    public async Task<IReadOnlyList<RefundRequest>> ListByUserAsync(Guid userId, CancellationToken cancellationToken = default)
        => await db.RefundRequests.Where(x => x.UserId == userId)
            .OrderByDescending(x => x.CreatedAtUtc).ToListAsync(cancellationToken);

    public async Task AddAsync(RefundRequest request, CancellationToken cancellationToken = default)
        => await db.RefundRequests.AddAsync(request, cancellationToken);

    public void Update(RefundRequest request) => db.RefundRequests.Update(request);
}

public sealed class OrganizerBankAccountRepository(FinanceDbContext db) : IOrganizerBankAccountRepository
{
    public Task<OrganizerBankAccount?> GetByUserAsync(Guid organizerUserId, CancellationToken cancellationToken = default)
        => db.OrganizerBankAccounts.FirstOrDefaultAsync(x => x.OrganizerUserId == organizerUserId, cancellationToken);

    public async Task AddAsync(OrganizerBankAccount account, CancellationToken cancellationToken = default)
        => await db.OrganizerBankAccounts.AddAsync(account, cancellationToken);

    public void Update(OrganizerBankAccount account) => db.OrganizerBankAccounts.Update(account);
}

public sealed class PlatformFinanceSettingsRepository(FinanceDbContext db) : IPlatformFinanceSettingsRepository
{
    public async Task<PlatformFinanceSettings> GetAsync(CancellationToken cancellationToken = default)
    {
        var s = await db.PlatformSettings.FirstOrDefaultAsync(
            x => x.Id == PlatformFinanceSettings.SingletonId, cancellationToken);
        if (s is not null)
        {
            return s;
        }

        s = PlatformFinanceSettings.CreateDefault();
        await db.PlatformSettings.AddAsync(s, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return s;
    }

    public void Update(PlatformFinanceSettings settings) => db.PlatformSettings.Update(settings);
}
