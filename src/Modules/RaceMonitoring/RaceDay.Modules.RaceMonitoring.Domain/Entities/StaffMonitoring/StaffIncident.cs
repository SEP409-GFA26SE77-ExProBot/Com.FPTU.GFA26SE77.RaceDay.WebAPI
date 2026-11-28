using RaceDay.Shared.Domain.Abstractions;
using RaceDay.Shared.Domain.Exceptions;

namespace RaceDay.Modules.RaceMonitoring.Domain.StaffMonitoring;

/// <summary>
/// Something the organizer should know about a Staff/Photographer during the race: the staff member left the
/// assigned zone (<see cref="StaffIncidentKind.OutOfZone"/>, written by the system) or sent a report from the app
/// (<see cref="StaffIncidentKind.Report"/>). Collected for display only; there is no handling workflow.
/// Name, role and zone are snapshots; ids refer to Event and IAM with no cross-schema foreign keys.
/// </summary>
public sealed class StaffIncident : AggregateRoot
{
    public Guid EventId { get; private set; }
    public Guid UserId { get; private set; }
    public StaffIncidentKind Kind { get; private set; }
    public string StaffName { get; private set; } = string.Empty;
    public string Role { get; private set; } = string.Empty;
    public string? ZoneName { get; private set; }
    public ContingencyIncidentType? IncidentType { get; private set; }
    public string? Description { get; private set; }
    public string? PhotoUrl { get; private set; }
    public double? Latitude { get; private set; }
    public double? Longitude { get; private set; }
    public double? AccuracyMeters { get; private set; }

    /// <summary>When the staff member left the zone, or when the report was sent.</summary>
    public DateTime OccurredAtUtc { get; private set; }

    /// <summary>Out-of-zone only: when the staff member was back inside. Null while still outside.</summary>
    public DateTime? ReturnedAtUtc { get; private set; }

    private StaffIncident()
    {
    }

    public static StaffIncident LeftZone(StaffIncidentAuthor staff, StaffIncidentLocation location, DateTime leftAtUtc)
    {
        ArgumentNullException.ThrowIfNull(location);
        return Create(staff, StaffIncidentKind.OutOfZone, location, leftAtUtc);
    }

    public static StaffIncident Report(
        StaffIncidentAuthor staff,
        ContingencyIncidentType incidentType,
        string? description,
        string? photoUrl,
        StaffIncidentLocation? location,
        DateTime reportedAtUtc)
    {
        var incident = Create(staff, StaffIncidentKind.Report, location, reportedAtUtc);
        incident.IncidentType = CoordinatorInvariants.RequiredEnum(incidentType, "Loại sự cố không hợp lệ.");
        incident.Description = CoordinatorInvariants.RequiredText(
            description,
            StaffIncidentFieldLengths.Description,
            "Mô tả sự cố không được để trống.",
            $"Mô tả sự cố tối đa {StaffIncidentFieldLengths.Description} ký tự.");
        incident.PhotoUrl = CoordinatorInvariants.OptionalText(
            photoUrl,
            StaffIncidentFieldLengths.PhotoUrl,
            "Đường dẫn ảnh quá dài.");
        return incident;
    }

    private static StaffIncident Create(
        StaffIncidentAuthor staff,
        StaffIncidentKind kind,
        StaffIncidentLocation? location,
        DateTime occurredAtUtc)
    {
        ArgumentNullException.ThrowIfNull(staff);
        CoordinatorInvariants.RequiredId(staff.EventId, "Tham chiếu giải không hợp lệ.");
        CoordinatorInvariants.RequiredId(staff.UserId, "Tham chiếu nhân sự không hợp lệ.");
        CoordinatorInvariants.RequiredTimestamp(occurredAtUtc, "Thời điểm sự cố không hợp lệ.");
        if (location is not null && !location.IsValid)
        {
            throw new DomainException("Vị trí sự cố không hợp lệ.");
        }

        return new StaffIncident
        {
            EventId = staff.EventId,
            UserId = staff.UserId,
            Kind = kind,
            StaffName = CoordinatorInvariants.RequiredText(
                staff.Name,
                CoordinatorFieldLengths.Name,
                "Thiếu tên nhân sự.",
                "Tên nhân sự vượt quá độ dài cho phép."),
            Role = CoordinatorInvariants.RequiredText(
                staff.Role,
                StaffIncidentFieldLengths.Role,
                "Thiếu vai trò nhân sự.",
                "Vai trò vượt quá độ dài cho phép."),
            ZoneName = Truncate(staff.ZoneName, StaffIncidentFieldLengths.ZoneName),
            Latitude = location?.Latitude,
            Longitude = location?.Longitude,
            AccuracyMeters = location?.AccuracyMeters,
            OccurredAtUtc = occurredAtUtc
        };
    }

    /// <summary>Zone labels come from the organizer's assignment; a long list is cut rather than refused.</summary>
    private static string? Truncate(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        return trimmed.Length <= maxLength ? trimmed : trimmed[..(maxLength - 1)] + "…";
    }
}

/// <param name="ZoneName">Labels of the assigned zones, for display.</param>
public sealed record StaffIncidentAuthor(Guid EventId, Guid UserId, string Name, string Role, string? ZoneName);

public sealed record StaffIncidentLocation(double Latitude, double Longitude, double? AccuracyMeters)
{
    public bool IsValid =>
        double.IsFinite(Latitude) && Latitude is >= -90 and <= 90
        && double.IsFinite(Longitude) && Longitude is >= -180 and <= 180
        && (AccuracyMeters is not { } accuracy || double.IsFinite(accuracy) && accuracy >= 0);
}

public static class StaffIncidentFieldLengths
{
    public const int Role = 100;
    public const int ZoneName = 500;
    public const int Description = 1000;
    public const int PhotoUrl = 1000;
}
