using RaceDay.Shared.Domain.Exceptions;

namespace RaceDay.Shared.Application.Dashboard;

/// <summary>Inclusive Vietnamese calendar dates, translated to a half-open UTC interval.</summary>
public sealed record DashboardDateRange(DateOnly From, DateOnly To)
{
    public const string TimeZone = "Asia/Ho_Chi_Minh";
    public DateTime FromUtc => DateTime.SpecifyKind(From.ToDateTime(TimeOnly.MinValue).AddHours(-7), DateTimeKind.Utc);
    public DateTime ToUtcExclusive => DateTime.SpecifyKind(To.AddDays(1).ToDateTime(TimeOnly.MinValue).AddHours(-7), DateTimeKind.Utc);

    public static DashboardDateRange Create(DateOnly? from, DateOnly? to, DateTime nowUtc)
    {
        var end = to ?? DateOnly.FromDateTime(nowUtc.AddHours(7));
        if (end.Year < 1900 || end.Year > 9998 || from is { Year: < 1900 or > 9998 })
            throw new DomainException("Ngày thống kê phải nằm trong khoảng năm 1900–9998.");
        var start = from ?? end.AddDays(-29);
        if (start > end || end.DayNumber - start.DayNumber >= 366)
            throw new DomainException("Khoảng thống kê phải có from ≤ to và không quá 366 ngày.");
        return new(start, end);
    }

    public IEnumerable<DateOnly> Days()
    {
        for (var date = From; date <= To; date = date.AddDays(1)) yield return date;
    }
}

public sealed record DashboardCount(string Status, int Count);
public sealed record DailyCount(DateOnly Date, int Count);
