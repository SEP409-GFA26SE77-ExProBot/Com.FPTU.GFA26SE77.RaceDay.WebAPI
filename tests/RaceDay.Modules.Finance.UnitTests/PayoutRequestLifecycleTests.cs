using RaceDay.Modules.Finance.Domain.Payouts;
using RaceDay.Shared.Domain.Exceptions;

namespace RaceDay.Modules.Finance.UnitTests;

public sealed class PayoutRequestLifecycleTests
{
    private static PayoutRequest NewPending() => PayoutRequest.Create(
        Guid.NewGuid(),
        Guid.NewGuid(),
        "RaceDay Grand Marathon",
        750_000m,
        "đợt 2",
        "VietinBank",
        "074205005592",
        "LE HOANG CUONG",
        "Ha Noi",
        "Giai ngan BTC");

    [Fact]
    public void Approve_then_confirm_transfer_with_receipt()
    {
        var p = NewPending();
        p.Approve();
        Assert.Equal(PayoutRequestStatus.Approved, p.Status);

        p.MarkTransferred("https://drive.example/receipt-001.png");
        Assert.Equal(PayoutRequestStatus.Transferred, p.Status);
        Assert.Equal("https://drive.example/receipt-001.png", p.ReceiptUrl);
        Assert.NotNull(p.TransferredAtUtc);
    }

    [Fact]
    public void Cannot_confirm_transfer_from_pending()
    {
        var p = NewPending();
        Assert.Throws<DomainException>(() => p.MarkTransferred("https://x"));
    }

    [Fact]
    public void Confirm_transfer_requires_receipt()
    {
        var p = NewPending();
        p.Approve();
        Assert.Throws<DomainException>(() => p.MarkTransferred("  "));
    }

    [Fact]
    public void Can_reject_approved_before_transfer()
    {
        var p = NewPending();
        p.Approve();
        p.Reject("Sai số tài khoản");
        Assert.Equal(PayoutRequestStatus.Rejected, p.Status);
        Assert.Equal("Sai số tài khoản", p.RejectReason);
    }
}
