using Microsoft.EntityFrameworkCore;
using RaceDay.Modules.Payment.Contracts.Payments;
using RaceDay.Modules.Payment.Domain;
using RaceDay.Modules.Payment.Infrastructure.Persistence;

namespace RaceDay.Modules.Payment.Infrastructure.Finance;

public sealed class PaymentRevenueQuery(PaymentDbContext db) : IPaymentRevenueQuery
{
    public async Task<IReadOnlyList<PaymentRevenueDay>> ReadAsync(IReadOnlyList<Guid> registrationIds,
        DateTime fromUtc, DateTime toUtcExclusive, CancellationToken ct)
    {
        if (registrationIds.Count == 0) return [];
        var ids = registrationIds.ToArray();
        var orders = db.PaymentOrders.AsNoTracking().Where(p => p.Purpose == PaymentPurpose.Registration
            && p.RegistrationId.HasValue && ids.Contains(p.RegistrationId.Value));
        // Each settled order is counted once, including orders subsequently refunded.
        var charges = await orders.Where(p => (p.Status == PaymentStatus.Paid || p.Status == PaymentStatus.Refunded)
                && p.PaidAtUtc >= fromUtc && p.PaidAtUtc < toUtcExclusive)
            .GroupBy(p => new { Date = p.PaidAtUtc!.Value.AddHours(7).Date, p.Currency })
            .Select(g => new { g.Key.Date, g.Key.Currency, Amount = g.Sum(p => p.Amount) }).ToListAsync(ct);
        // Use successful refund transaction time, not refund request creation/update time.
        var refunds = await (from t in db.PaymentTransactions.AsNoTracking()
                             join p in orders on t.PaymentId equals p.Id
                             where t.TransactionType == "refund" && t.Status == PaymentTransactionStatus.Succeeded
                                 && t.CreatedAtUtc >= fromUtc && t.CreatedAtUtc < toUtcExclusive
                             group t by new { Date = t.CreatedAtUtc.AddHours(7).Date, p.Currency } into g
                             select new { g.Key.Date, g.Key.Currency, Amount = g.Sum(t => t.Amount) }).ToListAsync(ct);
        return charges.Select(x => new PaymentRevenueDay(DateOnly.FromDateTime(x.Date), x.Currency, x.Amount, 0))
            .Concat(refunds.Select(x => new PaymentRevenueDay(DateOnly.FromDateTime(x.Date), x.Currency, 0, x.Amount)))
            .GroupBy(x => (x.Date, x.Currency))
            .Select(g => new PaymentRevenueDay(g.Key.Date, g.Key.Currency, g.Sum(x => x.Collected), g.Sum(x => x.Refunded)))
            .OrderBy(x => x.Date).ThenBy(x => x.Currency).ToArray();
    }
}
