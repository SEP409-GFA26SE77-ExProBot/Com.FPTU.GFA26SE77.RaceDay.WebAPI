using RaceDay.Shared.Application.Time;

namespace RaceDay.Modules.Event.UnitTests.Application;

public sealed class RaceLocalTimeTests
{
    [Fact]
    public void ToUtc_ReadsStoredClockAsVietnamTime()
        => Assert.Equal(new DateTime(2026, 11, 26, 23, 0, 0, DateTimeKind.Utc),
            RaceLocalTime.ToUtc(new DateOnly(2026, 11, 27), new TimeOnly(6, 0)));

    [Theory]
    [InlineData(16, 59, 26)] // 23:59 on 26/11 in Vietnam
    [InlineData(17, 0, 27)]  // 00:00 on 27/11 in Vietnam, still 26/11 in UTC
    [InlineData(23, 30, 27)] // 06:30 on 27/11 in Vietnam
    public void Today_IsTheVietnamCalendarDate(int utcHour, int utcMinute, int expectedDay)
        => Assert.Equal(new DateOnly(2026, 11, expectedDay),
            RaceLocalTime.Today(new DateTime(2026, 11, 26, utcHour, utcMinute, 0, DateTimeKind.Utc)));

    [Fact]
    public void TryFindZone_AlwaysResolvesVietnam()
    {
        Assert.True(RaceLocalTime.TryFindZone(" Asia/Ho_Chi_Minh ", out var zone));
        Assert.Equal(TimeSpan.FromHours(7), zone.GetUtcOffset(new DateTime(2026, 11, 27, 0, 0, 0, DateTimeKind.Utc)));
        Assert.False(RaceLocalTime.TryFindZone("Not/AZone", out _));
        Assert.False(RaceLocalTime.TryFindZone(null, out _));
    }
}
