namespace RaceDay.Modules.Map.Application.Courses;

public sealed record LatLngDto(double Lat, double Lng);

public sealed record RaceCheckpointDto(
    string Id,
    string Name,
    double Km,
    double Lat,
    double Lng,
    string Type,
    string? Cutoff = null);

/// <summary>Matches FE <c>CourseConfigDto</c> (organizer setup map + simulator).</summary>
public sealed record CourseConfigDto(
    string Title,
    Guid? DistanceId,
    double DistanceKm,
    IReadOnlyList<LatLngDto> Waypoints,
    IReadOnlyList<LatLngDto> Polyline,
    IReadOnlyList<RaceCheckpointDto> Checkpoints,
    DateTime UpdatedAt);

public sealed record UpsertCourseRequest(
    string? Title,
    Guid? DistanceId,
    double DistanceKm,
    IReadOnlyList<LatLngDto>? Waypoints,
    IReadOnlyList<LatLngDto>? Polyline,
    IReadOnlyList<RaceCheckpointDto>? Checkpoints);
