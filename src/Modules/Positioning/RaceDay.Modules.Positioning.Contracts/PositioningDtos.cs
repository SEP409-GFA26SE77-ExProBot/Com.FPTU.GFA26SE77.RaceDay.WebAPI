namespace RaceDay.Modules.Positioning.Contracts;

public sealed record ReportPositionBody(double? Latitude, double? Longitude, double? AccuracyMeters, DateTimeOffset? CapturedAtUtc);

public sealed record PositionReceipt(string ZoneStatus)
{
    /// <summary>Staff has not checked in by QR; the sample was not recorded.</summary>
    public static readonly PositionReceipt NotCheckedIn = new("not-checked-in");
}

public sealed record LiveStaffStation(string Id, string? Label);

public sealed record LiveStaffPosition(
    Guid UserId,
    string Name,
    IReadOnlyList<string> Roles,
    IReadOnlyList<LiveStaffStation> Stations,
    double Latitude,
    double Longitude,
    DateTimeOffset CapturedAtUtc);

public sealed record LiveStaffPositionPage(IReadOnlyList<LiveStaffPosition> Items, int? NextOffset);

public sealed record LivePositionFrame<T>(T Data, DateTimeOffset ServerNowUtc);

/// <summary>Out-of-zone alert. Delivered only to <see cref="UserId"/>, the staff member who left the zone.</summary>
public sealed record PositionAlert(Guid EventId, Guid UserId, Guid AssignmentId, string StaffName, string Role, DateTimeOffset OccurredAtUtc);

/// <summary>Rotating QR the organizer shows; staff scan it to check in. <see cref="Payload"/> is the QR text.</summary>
public sealed record StaffCheckInCode(string Payload, DateTimeOffset IssuedAtUtc, DateTimeOffset ExpiresAtUtc, DateTimeOffset ServerNowUtc);

public sealed record StaffCheckInBody(string? Code);

public sealed record StaffCheckInReceipt(DateTimeOffset CheckedInAtUtc);
