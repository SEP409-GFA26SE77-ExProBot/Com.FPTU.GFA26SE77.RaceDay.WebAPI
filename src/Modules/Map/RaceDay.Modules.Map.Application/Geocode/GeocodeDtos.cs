namespace RaceDay.Modules.Map.Application.Geocode;

public sealed record GeocodeResultDto(
    double Lat,
    double Lng,
    string Label,
    string Type,
    GeocodeBBoxDto? Bbox);

public sealed record GeocodeBBoxDto(
    double South,
    double North,
    double West,
    double East);

public sealed record GeocodeResponseDto(IReadOnlyList<GeocodeResultDto> Results, string? Error = null);
