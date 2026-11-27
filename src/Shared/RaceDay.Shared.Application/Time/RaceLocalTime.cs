namespace RaceDay.Shared.Application.Time;

/// <summary>
/// Race dates and clock times (EventDate, StartTime, distance StartAt/EndAt) are stored as
/// Vietnam wall-clock values: what organizers type in UserWeb and what every UI shows.
/// Convert through this class whenever such a value is compared with a UTC instant.
/// </summary>
public static class RaceLocalTime
{
    public const string TimeZoneId = "Asia/Ho_Chi_Minh";

    /// <summary>Asia/Ho_Chi_Minh, or a fixed UTC+07 zone when the host has no time zone data.</summary>
    public static TimeZoneInfo Zone { get; } = ResolveZone();

    public static DateTime ToUtc(DateOnly date, TimeOnly time)
        => DateTime.SpecifyKind(
            TimeZoneInfo.ConvertTimeToUtc(date.ToDateTime(time, DateTimeKind.Unspecified), Zone),
            DateTimeKind.Utc);

    /// <summary>The Vietnam calendar date at <paramref name="utcNow"/> (unspecified kind is read as UTC).</summary>
    public static DateOnly Today(DateTime utcNow)
        => DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(AsUtc(utcNow), Zone));

    /// <summary>Like <see cref="TimeZoneInfo.TryFindSystemTimeZoneById"/>, but the Vietnam zone always resolves.</summary>
    public static bool TryFindZone(string? id, out TimeZoneInfo zone)
    {
        var trimmed = id?.Trim();
        if (string.Equals(trimmed, TimeZoneId, StringComparison.OrdinalIgnoreCase))
        {
            zone = Zone;
            return true;
        }
        if (!string.IsNullOrEmpty(trimmed) && TimeZoneInfo.TryFindSystemTimeZoneById(trimmed, out var found))
        {
            zone = found;
            return true;
        }
        zone = TimeZoneInfo.Utc;
        return false;
    }

    private static DateTime AsUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
    };

    private static TimeZoneInfo ResolveZone()
    {
        foreach (var id in new[] { TimeZoneId, "SE Asia Standard Time" })
        {
            if (TimeZoneInfo.TryFindSystemTimeZoneById(id, out var zone)) return zone;
        }
        return TimeZoneInfo.CreateCustomTimeZone("UTC+07", TimeSpan.FromHours(7), "UTC+07", "UTC+07");
    }
}
