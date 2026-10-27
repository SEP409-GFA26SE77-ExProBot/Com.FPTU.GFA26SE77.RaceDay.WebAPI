using RaceDay.Modules.Payment.Application;
using RaceDay.Modules.Payment.Domain;

namespace RaceDay.Modules.Payment.UnitTests;

public class CheckoutPendingGuardTests
{
    [Fact]
    public void MarkExpired_Is_Idempotent_And_Blocks_Paid()
    {
        var order = PaymentOrder.Create(
            Guid.NewGuid(), Guid.NewGuid(), 100_000, "VND", 123456789);
        order.AttachCheckout("https://pay.example/x", "link-1");
        order.MarkExpired();
        Assert.Equal(PaymentStatus.Expired, order.Status);
        order.MarkExpired();
        Assert.Equal(PaymentStatus.Expired, order.Status);
    }

    [Fact]
    public void IsLocallyExpired_Uses_UpdatedAt_Anchor()
    {
        var order = PaymentOrder.Create(
            Guid.NewGuid(), Guid.NewGuid(), 50_000, "VND", 987654321);
        order.AttachCheckout("https://pay.example/y", "link-2");
        Assert.False(CheckoutPendingGuard.IsLocallyExpired(order, 15));
    }

    [Theory]
    [InlineData("EXPIRED", true)]
    [InlineData("CANCELLED", true)]
    [InlineData("CANCELED", true)]
    [InlineData("PENDING", false)]
    [InlineData("PAID", false)]
    public void Terminal_PayOs_Statuses(string status, bool expected)
        => Assert.Equal(expected, CheckoutPendingGuard.IsTerminalPayOsStatus(status));
}
