using RaceDay.Modules.RaceMonitoring.Application.CutoffWarnings;

namespace RaceDay.Modules.RaceMonitoring.UnitTests.Application;

public class CutoffClockTests
{
    private static readonly TimeZoneInfo Vietnam =
        TimeZoneInfo.CreateCustomTimeZone("test+07", TimeSpan.FromHours(7), "test+07", "test+07");

    private static readonly DateOnly RaceDay = new(2026, 11, 23);

    [Theory]
    [InlineData("07:00")]
    [InlineData("7:00")]
    [InlineData("07:00:00")]
    [InlineData(" 07h00 ")]
    public void ToUtc_LocalRaceDayTime_ConvertsFromVietnamTime(string cutoff)
    {
        var utc = CutoffClock.ToUtc(cutoff, RaceDay, null, Vietnam);

        Assert.Equal(new DateTime(2026, 11, 23, 0, 0, 0, DateTimeKind.Utc), utc);
        Assert.Equal(DateTimeKind.Utc, utc!.Value.Kind);
    }

    [Fact]
    public void ToUtc_CutoffBeforeDistanceStart_RollsToNextDay()
    {
        var utc = CutoffClock.ToUtc("02:00", RaceDay, new TimeOnly(21, 0), Vietnam);

        Assert.Equal(new DateTime(2026, 11, 23, 19, 0, 0, DateTimeKind.Utc), utc);
    }

    [Fact]
    public void ToUtc_IsoTimestamp_UsesItsOffset()
    {
        var utc = CutoffClock.ToUtc("2026-11-23T09:30:00+07:00", RaceDay, null, Vietnam);

        Assert.Equal(new DateTime(2026, 11, 23, 2, 30, 0, DateTimeKind.Utc), utc);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("TIMING")]
    [InlineData("25:00")]
    public void ToUtc_MissingOrInvalid_ReturnsNull(string? cutoff)
    {
        Assert.Null(CutoffClock.ToUtc(cutoff, RaceDay, null, Vietnam));
    }

    [Fact]
    public void ResolveZone_UnknownId_FallsBackToUtcPlus7()
    {
        var zone = CutoffClock.ResolveZone("Not/AZone");

        Assert.Equal(TimeSpan.FromHours(7), zone.GetUtcOffset(new DateTime(2026, 11, 23, 0, 0, 0, DateTimeKind.Utc)));
    }
}
