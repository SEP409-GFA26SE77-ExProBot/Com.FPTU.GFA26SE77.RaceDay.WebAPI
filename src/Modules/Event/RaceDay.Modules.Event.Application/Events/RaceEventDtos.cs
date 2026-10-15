using System.Text.Json;
using RaceDay.Modules.Event.Domain.Events;

namespace RaceDay.Modules.Event.Application.Events;

public sealed record GeoPointDto(double Lat, double Lng);
public sealed record NamedGeoPointDto(string Name, double Lat, double Lng);
public sealed record WeatherDto(string Temp, string Condition, string? Humidity);
public sealed record BeneficiaryAccountDto(
    string? AccountHolder,
    string? AccountNumber,
    string? BankName,
    string? Branch,
    string? SwiftCode,
    string? TransferNote);

public sealed record OrganizerDto(
    string Name,
    string Email,
    string Phone,
    string? LegalEntity,
    string? Website,
    bool Verified,
    BeneficiaryAccountDto? BeneficiaryAccount);

public sealed record PermitDocumentDto(
    string? FileUrl,
    string? FileName,
    string? PermitNumber,
    string? IssuingAuthority,
    DateOnly? IssueDate,
    DateOnly? ValidUntil);

public sealed record CheckpointDto(
    string Id,
    string Name,
    double Km,
    double Lat,
    double Lng,
    string Type,
    string? Cutoff,
    int? Elevation);

public sealed record DistanceDto(
    Guid Id,
    string Type,
    string Name,
    double DistanceKm,
    double ElevationGainM,
    double MaxElevationM,
    double MinElevationM,
    int RunnersCount,
    int Capacity,
    int RegisteredCount,
    decimal Price,
    string Currency,
    string Color,
    TimeOnly? StartAt,
    TimeOnly? EndAt,
    NamedGeoPointDto StartLocation,
    NamedGeoPointDto FinishLocation,
    IReadOnlyList<CheckpointDto> Checkpoints,
    JsonElement? OsrmWaypoints,
    JsonElement? LeafletPolyline,
    JsonElement? GeoJson);

public sealed record EventDocumentDto(
    string DocumentKey,
    string Title,
    string? FileName,
    string? FileUrl,
    bool Required);

public sealed record RaceEventDto(
    Guid Id,
    string Name,
    string? Tagline,
    string? BannerImage,
    string Location,
    GeoPointDto Coordinates,
    string Date,
    string Status,
    OrganizerDto Organizer,
    int TotalRunners,
    int ActiveRunners,
    WeatherDto? Weather,
    PermitDocumentDto? PermitDocument,
    string? Description,
    string? RejectionReason,
    TimeOnly? StartTime,
    string? EndDate,
    JsonElement? TicketPricing,
    JsonElement? Merchandise,
    JsonElement? Rules,
    JsonElement? Rewards,
    IReadOnlyList<DistanceDto> Distances,
    IReadOnlyList<EventDocumentDto> Documents,
    Guid? PackagePurchaseId,
    DateTime? PackageExpiresAtUtc,
    DateTime? RegistrationOpensAtUtc,
    DateTime? RegistrationClosesAtUtc,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc);

public sealed record RaceEventListItemDto(
    Guid Id,
    string Name,
    string? Tagline,
    string? BannerImage,
    string Location,
    string Date,
    string? EndDate,
    string Status,
    string OrganizerName,
    int TotalRunners,
    int ActiveRunners,
    /// <summary>Chip labels (tên cự ly / "12K") — không còn map Type mặt đường.</summary>
    IReadOnlyList<string> DistanceTypes,
    /// <summary>Km thực tế để FE filter bucket (~5 / ~10 / …).</summary>
    IReadOnlyList<double> DistanceKms,
    DateTime? RegistrationOpensAtUtc = null);

public static class RaceEventDtoMapper
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public static string ToApiStatus(RaceEventStatus status) => status switch
    {
        RaceEventStatus.Draft => "draft",
        RaceEventStatus.PendingApproval => "pending",
        RaceEventStatus.Approved => "approved",
        RaceEventStatus.Rejected => "rejected",
        RaceEventStatus.RegistrationOpen => "registration-open",
        RaceEventStatus.Closed => "closed",
        RaceEventStatus.Completed => "completed",
        RaceEventStatus.Cancelled => "cancelled",
        RaceEventStatus.Suspended => "suspended",
        _ => status.ToString().ToLowerInvariant()
    };

    public static CheckpointType ParseCheckpointType(string? type) => (type ?? string.Empty).Trim().ToLowerInvariant() switch
    {
        "start" => CheckpointType.Start,
        "finish" => CheckpointType.Finish,
        "water" => CheckpointType.Water,
        "aid" => CheckpointType.Aid,
        "medical" => CheckpointType.Medical,
        "cutoff" => CheckpointType.Cutoff,
        _ => CheckpointType.Aid
    };

    public static string ToApiCheckpointType(CheckpointType type) => type switch
    {
        CheckpointType.Start => "start",
        CheckpointType.Finish => "finish",
        CheckpointType.Water => "water",
        CheckpointType.Aid => "aid",
        CheckpointType.Medical => "medical",
        CheckpointType.Cutoff => "cutoff",
        _ => "aid"
    };

    public static RaceEventListItemDto ToListItem(RaceEvent e) => new(
        e.Id,
        e.Name,
        e.Tagline,
        e.BannerImage,
        e.Location,
        e.EventDate.ToString("yyyy-MM-dd"),
        e.EndDate?.ToString("yyyy-MM-dd"),
        ToApiStatus(e.Status),
        e.Organizer.Name,
        e.TotalRunnersCap,
        e.ActiveRunners,
        e.Distances
            .Select(d =>
                !string.IsNullOrWhiteSpace(d.Name)
                    ? d.Name.Trim()
                    : d.DistanceKm > 0
                        ? $"{d.DistanceKm}K"
                        : d.Type)
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList(),
        e.Distances
            .Select(d => d.DistanceKm)
            .Where(km => km > 0)
            .ToList(),
        e.RegistrationOpensAtUtc);

    public static RaceEventDto ToDetail(RaceEvent e)
    {
        return new RaceEventDto(
            e.Id,
            e.Name,
            e.Tagline,
            e.BannerImage,
            e.Location,
            new GeoPointDto(e.Coordinates.Lat, e.Coordinates.Lng),
            e.EventDate.ToString("yyyy-MM-dd"),
            ToApiStatus(e.Status),
            new OrganizerDto(
                e.Organizer.Name,
                e.Organizer.Email,
                e.Organizer.Phone,
                e.Organizer.LegalEntity,
                e.Organizer.Website,
                e.Organizer.Verified,
                new BeneficiaryAccountDto(
                    e.Organizer.AccountHolder,
                    e.Organizer.AccountNumber,
                    e.Organizer.BankName,
                    e.Organizer.BankBranch,
                    e.Organizer.SwiftCode,
                    e.Organizer.TransferNote)),
            e.TotalRunnersCap,
            e.ActiveRunners,
            // Soft-surplus: Weather / Permit owned / Merchandise — FE không dùng; SoT giấy phép = event_documents
            Weather: null,
            PermitDocument: null,
            e.Description,
            e.RejectionReason,
            e.StartTime,
            e.EndDate?.ToString("yyyy-MM-dd"),
            ParseJson(e.TicketPricingJson),
            Merchandise: null,
            ParseJson(e.RulesJson),
            ParseJson(e.RewardsJson),
            e.Distances.Select(MapDistance).ToList(),
            e.Documents.Select(d => new EventDocumentDto(d.DocumentKey, d.Title, d.FileName, d.FileUrl, d.Required)).ToList(),
            e.PackagePurchaseId,
            e.PackageExpiresAtUtc,
            e.RegistrationOpensAtUtc,
            e.RegistrationClosesAtUtc,
            e.CreatedAtUtc,
            e.UpdatedAtUtc);
    }

    private static DistanceDto MapDistance(EventDistance d)
    {
        using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(d.RouteGeometryJson) ? "{}" : d.RouteGeometryJson);
        var root = doc.RootElement;
        JsonElement? osrm = root.TryGetProperty("osrmWaypoints", out var o) ? o.Clone() : null;
        JsonElement? poly = root.TryGetProperty("leafletPolyline", out var p) ? p.Clone() : null;
        JsonElement? geo = root.TryGetProperty("geoJson", out var g) ? g.Clone() : null;

        return new DistanceDto(
            d.Id,
            d.Type,
            d.Name,
            d.DistanceKm,
            d.ElevationGainM,
            d.MaxElevationM,
            d.MinElevationM,
            d.RegisteredCount,
            d.Capacity,
            d.RegisteredCount,
            d.Price,
            d.Currency,
            d.Color,
            d.StartAt,
            d.EndAt,
            new NamedGeoPointDto(d.StartLocation.Name, d.StartLocation.Lat, d.StartLocation.Lng),
            new NamedGeoPointDto(d.FinishLocation.Name, d.FinishLocation.Lat, d.FinishLocation.Lng),
            // Soft-surplus: event_checkpoints legacy — SoT = Map CourseJson
            Array.Empty<CheckpointDto>(),
            osrm,
            poly,
            geo);
    }

    private static JsonElement? ParseJson(string json)
    {
        if (string.IsNullOrWhiteSpace(json) || json is "{}")
        {
            return null;
        }

        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }

    public static string SerializeOptional(object? value)
        => value is null ? "{}" : JsonSerializer.Serialize(value, JsonOptions);
}
