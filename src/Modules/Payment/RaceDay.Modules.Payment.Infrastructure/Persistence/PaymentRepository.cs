using Microsoft.EntityFrameworkCore;
using RaceDay.Modules.Payment.Application;
using RaceDay.Modules.Payment.Domain;

namespace RaceDay.Modules.Payment.Infrastructure.Persistence;

public sealed class PaymentRepository(PaymentDbContext db) : IPaymentRepository
{
    public Task<PaymentOrder?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        => db.PaymentOrders.Include(x => x.Transactions).FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

    public Task<PaymentOrder?> GetByOrderCodeAsync(long orderCode, CancellationToken cancellationToken = default)
        => db.PaymentOrders.Include(x => x.Transactions).FirstOrDefaultAsync(x => x.OrderCode == orderCode, cancellationToken);

    public async Task<PaymentOrder?> GetByIdForUpdateAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"""SELECT 1 FROM payment.payments WHERE "Id" = {id} FOR UPDATE""",
            cancellationToken);

        return await GetByIdAsync(id, cancellationToken);
    }

    public async Task<PaymentOrder?> GetByOrderCodeForUpdateAsync(long orderCode, CancellationToken cancellationToken = default)
    {
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"""SELECT 1 FROM payment.payments WHERE "OrderCode" = {orderCode} FOR UPDATE""",
            cancellationToken);

        return await GetByOrderCodeAsync(orderCode, cancellationToken);
    }

    public Task<PaymentOrder?> GetPendingByRegistrationAsync(Guid registrationId, CancellationToken cancellationToken = default)
        => db.PaymentOrders
            .Include(x => x.Transactions)
            .Where(x => x.RegistrationId == registrationId && x.Status == PaymentStatus.Pending)
            .OrderByDescending(x => x.CreatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

    public Task<PaymentOrder?> GetPendingByReferenceAsync(PaymentPurpose purpose, Guid referenceId, CancellationToken cancellationToken = default)
        => db.PaymentOrders
            .Include(x => x.Transactions)
            .Where(x => x.Purpose == purpose && x.ReferenceId == referenceId && x.Status == PaymentStatus.Pending)
            .OrderByDescending(x => x.CreatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<PaymentOrder>> ListByUserAsync(Guid userId, CancellationToken cancellationToken = default)
        => await db.PaymentOrders
            .Where(x => x.UserId == userId)
            .OrderByDescending(x => x.CreatedAtUtc)
            .ToListAsync(cancellationToken);

    public async Task AddAsync(PaymentOrder payment, CancellationToken cancellationToken = default)
        => await db.PaymentOrders.AddAsync(payment, cancellationToken);

    public void AddTransaction(PaymentTransaction transaction)
        => db.PaymentTransactions.Add(transaction);

    public void Update(PaymentOrder payment) => db.PaymentOrders.Update(payment);
}

public sealed class PaymentUnitOfWork(PaymentDbContext db) : IPaymentUnitOfWork
{
    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        => db.SaveChangesAsync(cancellationToken);

    public async Task<T> ExecuteInTransactionAsync<T>(
        Func<CancellationToken, Task<T>> action,
        CancellationToken cancellationToken = default)
    {
        if (db.Database.CurrentTransaction is not null)
        {
            var nested = await action(cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
            return nested;
        }

        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var result = await action(cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
            await tx.CommitAsync(cancellationToken);
            return result;
        }
        catch
        {
            await tx.RollbackAsync(cancellationToken);
            throw;
        }
    }
}
