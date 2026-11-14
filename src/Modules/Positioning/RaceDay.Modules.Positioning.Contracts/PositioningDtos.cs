namespace RaceDay.Modules.Positioning.Contracts;

public sealed record ReportPositionBody(double? Latitude, double? Longitude, double? AccuracyMeters, DateTimeOffset? CapturedAtUtc);

public sealed record PositionReceipt(string ZoneStatus);

public sealed record PositionAlert(Guid EventId, Guid AssignmentId, string StaffName, string Role, DateTimeOffset OccurredAtUtc);
