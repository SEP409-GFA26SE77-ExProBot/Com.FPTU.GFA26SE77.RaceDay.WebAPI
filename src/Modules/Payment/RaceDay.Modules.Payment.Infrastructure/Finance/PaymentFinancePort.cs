using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using RaceDay.Modules.Payment.Application;
using RaceDay.Modules.Payment.Contracts.Payments;
using RaceDay.Modules.Payment.Domain;
using RaceDay.Modules.Payment.Infrastructure.Persistence;
using RaceDay.Modules.Payment.Infrastructure.PayOs;
using RaceDay.Shared.Domain.Exceptions;

namespace RaceDay.Modules.Payment.Infrastructure.Finance;

public sealed class PaymentFinancePort(
    PaymentDbContext db,
    IPayOsGateway payOs,
    ILogger<PaymentFinancePort> logger) : IPaymentFinancePort
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

    public async Task<PaymentFinanceInfo?> GetSettledByRegistrationAsync(
        Guid registrationId,
        CancellationToken cancellationToken = default)
    {
        var p = await db.PaymentOrders.AsNoTracking()
            .Where(x => x.RegistrationId == registrationId
                        && (x.Status == PaymentStatus.Paid || x.Status == PaymentStatus.Refunded))
            .OrderByDescending(x => x.PaidAtUtc ?? x.UpdatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);
        return p is null ? null : Map(p);
    }

    public async Task MarkRefundedAsync(
        Guid paymentId,
        decimal refundAmount,
        CancellationToken cancellationToken = default)
    {
        var p = await db.PaymentOrders
                    .Include(x => x.Transactions)
                    .FirstOrDefaultAsync(x => x.Id == paymentId, cancellationToken)
                ?? throw new DomainException("Không tìm thấy payment.");

        if (p.Status == PaymentStatus.Refunded)
        {
            return;
        }

        if (p.Status != PaymentStatus.Paid)
        {
            throw new DomainException("Chỉ hoàn được payment đã thanh toán.");
        }

        var amountVnd = (int)Math.Round(refundAmount, MidpointRounding.AwayFromZero);
        if (amountVnd <= 0)
        {
            throw new DomainException("Số tiền hoàn phải > 0.");
        }

        var chargeRaw = p.Transactions
            .Where(t => string.Equals(t.TransactionType, "charge", StringComparison.OrdinalIgnoreCase)
                        && t.Status == PaymentTransactionStatus.Succeeded)
            .OrderByDescending(t => t.CreatedAtUtc)
            .Select(t => t.RawResponseJson)
            .FirstOrDefault();

        var (bin, account) = PayOsGateway.TryExtractCounterAccount(chargeRaw);

        var gatewayResult = await payOs.RefundAsync(
            new PayOsRefundRequest(
                p.Id,
                p.OrderCode,
                amountVnd,
                $"Hoan RD {p.OrderCode}",
                bin,
                account,
                chargeRaw),
            cancellationToken);

        var tx = p.MarkRefunded(refundAmount, gatewayResult.PayoutId, gatewayResult.RawResponseJson);
        db.PaymentTransactions.Add(tx);
        await db.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Payment {PaymentId} marked Refunded via PayOS {Mode} payoutId={PayoutId} amount={Amount}",
            paymentId,
            gatewayResult.IsMock ? "mock" : "live",
            gatewayResult.PayoutId,
            refundAmount);
    }

    private static PaymentFinanceInfo Map(PaymentOrder p) => new(
        p.Id,
        p.RegistrationId ?? Guid.Empty,
        p.UserId,
        p.Amount,
        p.Currency,
        p.Status.ToString(),
        p.PaidAtUtc);
}
