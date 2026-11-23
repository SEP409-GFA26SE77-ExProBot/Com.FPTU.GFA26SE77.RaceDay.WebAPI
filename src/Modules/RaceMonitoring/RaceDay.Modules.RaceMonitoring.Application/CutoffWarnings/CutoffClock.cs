using System.Globalization;

namespace RaceDay.Modules.RaceMonitoring.Application.CutoffWarnings;

/// <summary>
/// Turns the organizer's checkpoint cut-off ("HH:mm" local race-day time) into UTC.
/// A full ISO timestamp with an offset is also accepted.
/// </summary>
public static class CutoffClock
{
    public const string DefaultTimeZoneId = "Asia/Ho_Chi_Minh";

    private static readonly string[] TimeFormats = ["H:mm", "HH:mm", "H:mm:ss", "HH:mm:ss", "H'h'mm", "HH'h'mm"];

    public static DateTime? ToUtc(string? cutoff, DateOnly eventDate, TimeOnly? distanceStart, TimeZoneInfo zone)
    {
        if (string.IsNullOrWhiteSpace(cutoff))
        {
            return null;
        }

        var raw = cutoff.Trim();
        if (TimeOnly.TryParseExact(raw, TimeFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var time))
        {
            // Ultra distances can start in the evening and close after midnight.
            var day = distanceStart is { } start && time < start ? eventDate.AddDays(1) : eventDate;
            var local = day.ToDateTime(time, DateTimeKind.Unspecified);
            return TimeZoneInfo.ConvertTimeToUtc(local, zone);
        }

        if (raw.Contains('T')
            && DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var exact))
        {
            return exact.UtcDateTime;
        }

        return null;
    }

    public static TimeZoneInfo ResolveZone(string? timeZoneId)
    {
        foreach (var id in new[] { timeZoneId, DefaultTimeZoneId, "SE Asia Standard Time" })
        {
            if (!string.IsNullOrWhiteSpace(id) && TimeZoneInfo.TryFindSystemTimeZoneById(id.Trim(), out var zone))
            {
                return zone;
            }
        }

        return TimeZoneInfo.CreateCustomTimeZone("UTC+07", TimeSpan.FromHours(7), "UTC+07", "UTC+07");
    }
}

/// <param name="GracePeriod">Wait after cut-off before warning, so delayed RFID uploads can land first.</param>
/// <param name="LookBack">Cut-offs older than this are not used to raise new warnings.</param>
/// <param name="RequireStartEvidence">
/// Only warn for runners with a scan, a start time or race status Running, so no-shows do not flood the list.
/// </param>
public sealed record CutoffMonitoringSettings(
    TimeSpan GracePeriod,
    TimeSpan LookBack,
    bool RequireStartEvidence,
    TimeZoneInfo TimeZone);
