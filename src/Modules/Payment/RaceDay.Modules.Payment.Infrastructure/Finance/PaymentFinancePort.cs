using Microsoft.EntityFrameworkCore;
using RaceDay.Modules.Payment.Contracts.Payments;
using RaceDay.Modules.Payment.Domain;
using RaceDay.Modules.Payment.Infrastructure.Persistence;
using RaceDay.Shared.Domain.Exceptions;

namespace RaceDay.Modules.Payment.Infrastructure.Finance;

public sealed class PaymentFinancePort(PaymentDbContext db) : IPaymentFinancePort
{
    public async Task<PaymentFinanceInfo?> GetByIdAsync(Guid paymentId, CancellationToken cancellationToken = default)
    {
        var p = await db.PaymentOrders.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == paymentId, cancellationToken);
        return p is null ? null : Map(p);
    }

    public async Task<PaymentFinanceInfo?> GetPaidByRegistrationAsync(
        Guid registrationId,
        CancellationToken cancellationToken = default)
    {
        var p = await db.PaymentOrders.AsNoTracking()
            .Where(x => x.RegistrationId == registrationId && x.Status == PaymentStatus.Paid)
            .OrderByDescending(x => x.PaidAtUtc)
            .FirstOrDefaultAsync(cancellationToken);
        return p is null ? null : Map(p);
    }

    public async Task MarkRefundedAsync(Guid paymentId, CancellationToken cancellationToken = default)
    {
        var p = await db.PaymentOrders.FirstOrDefaultAsync(x => x.Id == paymentId, cancellationToken)
            ?? throw new DomainException("Không tìm thấy payment.");
        p.MarkRefunded();
        await db.SaveChangesAsync(cancellationToken);
    }

    private static PaymentFinanceInfo Map(PaymentOrder p) => new(
        p.Id,
        p.RegistrationId,
        p.UserId,
        p.Amount,
        p.Currency,
        p.Status.ToString(),
        p.PaidAtUtc);
}