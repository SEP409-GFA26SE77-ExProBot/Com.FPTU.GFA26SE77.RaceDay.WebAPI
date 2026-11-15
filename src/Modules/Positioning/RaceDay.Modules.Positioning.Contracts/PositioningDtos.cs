namespace RaceDay.Modules.Positioning.Contracts;

public sealed record ReportPositionBody(double? Latitude, double? Longitude, double? AccuracyMeters, DateTimeOffset? CapturedAtUtc);

public sealed record PositionReceipt(string ZoneStatus);

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

public sealed record PositionAlert(Guid EventId, Guid AssignmentId, string StaffName, string Role, DateTimeOffset OccurredAtUtc);
