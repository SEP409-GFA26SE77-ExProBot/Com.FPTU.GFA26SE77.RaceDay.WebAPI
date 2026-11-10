using RaceDay.Modules.Event.Contracts.Finance;
using RaceDay.Modules.Finance.Application.Wallet;
using RaceDay.Modules.Finance.Domain.Ledger;
using RaceDay.Modules.Finance.Domain.Settings;

namespace RaceDay.Modules.Finance.UnitTests;

public sealed class LedgerPolicyTests
{
    private static PlatformFinanceSettings Settings() => PlatformFinanceSettings.CreateDefault();

    private static EventFinanceInfo EventOn(DateOnly date, string status = "RegistrationOpen")
        => new(
            Guid.NewGuid(),
            "Test Race",
            Guid.NewGuid(),
            status,
            date,
            new TimeOnly(6, 0),
            null,
            null,
            null,
            null,
            null);

    /// <summary>
    /// Organizer reject dùng cùng ComputeRefund: vẫn hoàn theo ngày; 0% vẫn hủy slot (handler riêng).
    /// </summary>
    [Theory]
    [InlineData(100, 100, 500_000)] // ≥90d → 100%
    [InlineData(75, 85, 510_000)] // ≥60d → 85% of 600k
    [InlineData(45, 0, 0)] // <60d → 0
    [InlineData(20, 0, 0)] // T-4w lock → 0
    public void ComputeRefund_ByDaysUntilEvent(int daysUntil, decimal expectedPercent, decimal expectedAmount)
    {
        var paid = expectedPercent == 100 ? 500_000m : 600_000m;
        var now = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);
        var eventDate = DateOnly.FromDateTime(now.Date.AddDays(daysUntil));
        var ev = EventOn(eventDate);
        var (percent, amount) = LedgerCalculator.ComputeRefund(paid, Settings(), ev, now);
        Assert.Equal(expectedPercent, percent);
        Assert.Equal(expectedAmount, amount);
    }

    [Fact]
    public void ComputeRefund_AfterStart_IsZero()
    {
        var now = new DateTime(2026, 6, 10, 8, 0, 0, DateTimeKind.Utc);
        var ev = EventOn(new DateOnly(2026, 6, 10));
        var (percent, amount) = LedgerCalculator.ComputeRefund(500_000m, Settings(), ev, now);
        Assert.Equal(0, percent);
        Assert.Equal(0, amount);
    }

    [Fact]
    public void Wallet_TooEarly_AvailableIsZero()
    {
        var ledger = EventLedger.Create(Guid.NewGuid(), Guid.NewGuid(), "Race", "VND");
        ledger.CreditTicketSale(1_000_000m);
        var nowDate = DateOnly.FromDateTime(DateTime.UtcNow.Date.AddDays(120));
        var dto = LedgerCalculator.ToDto(ledger, Settings(), EventOn(nowDate));
        Assert.Equal(LedgerCalculator.PhaseTooEarly, dto.WithdrawPhase);
        Assert.Equal(0, dto.AvailableToWithdraw);
    }

    [Fact]
    public void Wallet_AdvancePhase_CapsAt30PercentNet()
    {
        var ledger = EventLedger.Create(Guid.NewGuid(), Guid.NewGuid(), "Race", "VND");
        ledger.CreditTicketSale(1_000_000m);
        // net = 1_000_000 - 5% = 950_000; advance 30% = 285_000
        var eventDate = DateOnly.FromDateTime(DateTime.UtcNow.Date.AddDays(14));
        var dto = LedgerCalculator.ToDto(ledger, Settings(), EventOn(eventDate, "Closed"));
        Assert.Equal(LedgerCalculator.PhaseAdvance, dto.WithdrawPhase);
        Assert.Equal(285_000m, dto.AdvanceCapAmount);
        Assert.Equal(285_000m, dto.AvailableToWithdraw);
    }

    [Fact]
    public void Wallet_FinalPhase_AllowsRemainingEscrow()
    {
        var ledger = EventLedger.Create(Guid.NewGuid(), Guid.NewGuid(), "Race", "VND");
        ledger.CreditTicketSale(1_000_000m);
        ledger.ReservePayout(285_000m);
        ledger.CompletePayout(285_000m);
        var eventDate = DateOnly.FromDateTime(DateTime.UtcNow.Date.AddDays(-1));
        var dto = LedgerCalculator.ToDto(ledger, Settings(), EventOn(eventDate, "Closed"));
        Assert.Equal(LedgerCalculator.PhaseFinal, dto.WithdrawPhase);
        // net 950_000 - withdrawn 285_000 = 665_000 escrow
        Assert.Equal(665_000m, dto.AvailableToWithdraw);
    }

    [Fact]
    public void PlatformDefaults_MatchDoc()
    {
        var s = Settings();
        Assert.Equal(30m, s.PreEventWithdrawPercent);
        Assert.Equal(70m, s.PostEventWithdrawPercent);
        Assert.Equal(100m, s.FullRefundPercent);
        Assert.Equal(85m, s.PartialRefundPercent);
    }
}
