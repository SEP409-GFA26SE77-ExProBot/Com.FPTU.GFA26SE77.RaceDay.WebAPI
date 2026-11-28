namespace RaceDay.Modules.RaceMonitoring.Contracts.StaffIncidents;

/// <param name="ZoneName">Labels of the zones the staff member is assigned to, for display.</param>
public sealed record StaffIncidentStaff(Guid EventId, Guid UserId, string Name, string Role, string? ZoneName);

public sealed record StaffIncidentPoint(double Latitude, double Longitude, double? AccuracyMeters);

/// <param name="IncidentType">One of <see cref="StaffIncidentTypes.All"/>.</param>
public sealed record StaffReportInput(
    StaffIncidentStaff Staff,
    string IncidentType,
    string Description,
    string? PhotoUrl,
    StaffIncidentPoint? Location,
    DateTimeOffset ReportedAtUtc);

/// <summary>
/// Positioning → race_monitoring.staff_incidents. Out-of-zone rows are written only when the zone state changes,
/// never per GPS sample.
/// </summary>
public interface IStaffIncidentSink
{
    /// <summary>Opens an out-of-zone row. No-op when this staff member already has one open for the event.</summary>
    Task RecordLeftZoneAsync(
        StaffIncidentStaff staff,
        StaffIncidentPoint location,
        DateTimeOffset leftAtUtc,
        CancellationToken cancellationToken = default);

    /// <summary>Closes the open out-of-zone row, if any.</summary>
    Task RecordReturnedAsync(
        Guid eventId,
        Guid userId,
        DateTimeOffset returnedAtUtc,
        CancellationToken cancellationToken = default);

    Task<StaffIncidentItem> RecordReportAsync(StaffReportInput report, CancellationToken cancellationToken = default);
}

public interface IStaffIncidentQuery
{
    Task<StaffIncidentPage> ListAsync(StaffIncidentFilter filter, CancellationToken cancellationToken = default);
}

/// <param name="Kind">Null for both, else <see cref="StaffIncidentKinds"/>.</param>
/// <param name="Search">Matches staff name, role, zone or description.</param>
public sealed record StaffIncidentFilter(Guid EventId, string? Kind, string? Search, int Page, int PageSize);

public sealed record StaffIncidentItem(
    Guid Id,
    string Kind,
    Guid UserId,
    string StaffName,
    string Role,
    string? ZoneName,
    string? IncidentType,
    string? Description,
    string? PhotoUrl,
    double? Latitude,
    double? Longitude,
    double? AccuracyMeters,
    DateTimeOffset OccurredAtUtc,
    DateTimeOffset? ReturnedAtUtc);

/// <param name="CurrentlyOutsideCount">Out-of-zone rows still open.</param>
public sealed record StaffIncidentPage(
    IReadOnlyList<StaffIncidentItem> Items,
    int Total,
    int Page,
    int PageSize,
    int OutOfZoneCount,
    int ReportCount,
    int CurrentlyOutsideCount);

public static class StaffIncidentKinds
{
    public const string OutOfZone = "OutOfZone";
    public const string Report = "Report";
}

/// <summary>Report types staff can pick (names of RaceMonitoring's ContingencyIncidentType).</summary>
public static class StaffIncidentTypes
{
    public static readonly IReadOnlyList<string> All =
        ["Weather", "CourseHazard", "Medical", "Security", "Logistics", "Other"];
}
