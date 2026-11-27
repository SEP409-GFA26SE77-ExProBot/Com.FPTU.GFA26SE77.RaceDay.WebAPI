using MediatR;
using RaceDay.Modules.Event.Contracts.Events;
using RaceDay.Modules.Positioning.Domain;
using RaceDay.Modules.RaceMonitoring.Contracts.StaffIncidents;
using RaceDay.Shared.Application.Abstractions;
using RaceDay.Shared.Domain.Exceptions;

namespace RaceDay.Modules.Positioning.Application;

public static class StaffZones
{
    /// <summary>Labels of the assigned zones ("Trạm nước km 5, Theo đoàn"), or null when none has a label.</summary>
    public static string? Describe(IEnumerable<PositioningStaffAssignment> assignments)
    {
        var labels = assignments
            .SelectMany(assignment => assignment.Stations)
            .Select(station => station.Label?.Trim())
            .OfType<string>()
            .Where(label => label.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return labels.Length == 0 ? null : string.Join(", ", labels);
    }
}

public sealed record StaffReportPhoto(Stream Content, string FileName, string ContentType, long Length);

/// <summary>
/// A Staff/Photographer reports an incident to the organizer during the race. Returns null when the caller has no
/// active assignment at the event. Check-in is not required: the incident may be why the staff member cannot.
/// </summary>
public sealed record SubmitStaffReportCommand(
    Guid EventId,
    Guid UserId,
    string? IncidentType,
    string? Description,
    double? Latitude,
    double? Longitude,
    double? AccuracyMeters,
    StaffReportPhoto? Photo) : IRequest<StaffIncidentItem?>;

public sealed class SubmitStaffReportCommandHandler(
    PositioningAccess access,
    IStaffIncidentSink incidents,
    IFileStorage storage,
    TimeProvider clock) : IRequestHandler<SubmitStaffReportCommand, StaffIncidentItem?>
{
    public const int MaxDescriptionLength = 1000;
    public const long MaxPhotoBytes = 5 * 1024 * 1024;
    public const string PhotoFolder = "staff-reports";
    private static readonly string[] PhotoTypes = ["image/jpeg", "image/png", "image/webp", "image/heic", "image/heif"];

    public async Task<StaffIncidentItem?> Handle(SubmitStaffReportCommand request, CancellationToken cancellationToken)
    {
        var reporting = await access.GetReportingAccessAsync(request.EventId, request.UserId, cancellationToken);
        if (reporting is null || reporting.Assignments.Count == 0) return null;

        var now = clock.GetUtcNow();
        if (reporting.AlertsStartAt is not { } opensAt)
        {
            throw new DomainException("Giải chưa có giờ xuất phát, đã kết thúc, bị hủy hoặc tạm khóa nên không gửi được báo cáo.");
        }
        if (now < opensAt)
        {
            throw new DomainException("Báo cáo sự cố mở từ 1 giờ trước giờ xuất phát.");
        }

        var type = request.IncidentType?.Trim();
        if (type is null || !StaffIncidentTypes.All.Contains(type))
        {
            throw new DomainException("Chọn loại sự cố.");
        }

        var description = request.Description?.Trim();
        if (string.IsNullOrEmpty(description))
        {
            throw new DomainException("Mô tả sự cố không được để trống.");
        }
        if (description.Length > MaxDescriptionLength)
        {
            throw new DomainException($"Mô tả sự cố tối đa {MaxDescriptionLength} ký tự.");
        }

        var location = Location(request);
        // Every check runs before the upload so a refused report leaves no orphan file.
        var photoUrl = request.Photo is { } photo ? await UploadAsync(photo, cancellationToken) : null;

        var assignments = reporting.Assignments;
        var staff = new StaffIncidentStaff(
            request.EventId,
            request.UserId,
            assignments[0].Name,
            string.Join(", ", assignments.Select(assignment => assignment.Role).Distinct().Order()),
            StaffZones.Describe(assignments));
        return await incidents.RecordReportAsync(
            new StaffReportInput(staff, type, description, photoUrl, location, now),
            cancellationToken);
    }

    private static StaffIncidentPoint? Location(SubmitStaffReportCommand request)
    {
        if (request is { Latitude: null, Longitude: null }) return null;
        if (request is not { Latitude: double lat, Longitude: double lng }
            || !double.IsFinite(lat) || !double.IsFinite(lng) || !new GeoPoint(lat, lng).IsValid
            || request.AccuracyMeters is { } accuracy && (!double.IsFinite(accuracy) || accuracy < 0))
        {
            throw new DomainException("Vị trí báo cáo không hợp lệ.");
        }

        return new StaffIncidentPoint(lat, lng, request.AccuracyMeters);
    }

    private async Task<string> UploadAsync(StaffReportPhoto photo, CancellationToken cancellationToken)
    {
        if (photo.Length <= 0 || photo.Length > MaxPhotoBytes)
        {
            throw new DomainException("Ảnh đính kèm phải nhỏ hơn 5 MB.");
        }
        var contentType = photo.ContentType.Trim().ToLowerInvariant();
        if (!PhotoTypes.Contains(contentType))
        {
            throw new DomainException("Ảnh đính kèm phải là JPG, PNG, WEBP hoặc HEIC.");
        }

        var stored = await storage.UploadAsync(photo.Content, photo.FileName, contentType, PhotoFolder, cancellationToken);
        return stored.FileUrl;
    }
}

/// <summary>Organizer of the event or Admin. Returns null for anyone else.</summary>
public sealed record ListStaffIncidentsQuery(
    Guid EventId,
    Guid UserId,
    bool IsAdmin,
    string? Kind,
    string? Search,
    int Page,
    int PageSize) : IRequest<StaffIncidentPage?>;

public sealed class ListStaffIncidentsQueryHandler(PositioningAccess access, IStaffIncidentQuery incidents)
    : IRequestHandler<ListStaffIncidentsQuery, StaffIncidentPage?>
{
    public async Task<StaffIncidentPage?> Handle(ListStaffIncidentsQuery request, CancellationToken cancellationToken)
    {
        var race = await access.GetRaceAsync(request.EventId, cancellationToken);
        if (race is null || !request.IsAdmin && race.OrganizerUserId != request.UserId) return null;

        var kind = string.IsNullOrWhiteSpace(request.Kind) ? null : request.Kind.Trim();
        return await incidents.ListAsync(
            new StaffIncidentFilter(request.EventId, kind, request.Search, request.Page, request.PageSize),
            cancellationToken);
    }
}
