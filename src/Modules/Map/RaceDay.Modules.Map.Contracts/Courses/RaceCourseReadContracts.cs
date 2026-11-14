namespace RaceDay.Modules.Map.Contracts.Courses;

public sealed record RaceCoursePoint(double Lat, double Lng);

public sealed record RaceCourseCheckpoint(
    string Id,
    string Name,
    double Km,
    double Lat,
    double Lng,
    string Type);

/// <summary>Read-only course captured when a simulation run is created. Opening a run does not write the course.</summary>
public sealed record RaceCourseSnapshot(
    Guid EventId,
    Guid DistanceId,
    string Title,
    double DistanceKm,
    IReadOnlyList<RaceCoursePoint> Polyline,
    IReadOnlyList<RaceCourseCheckpoint> Checkpoints);

public interface IRaceCourseReadQuery
{
    Task<RaceCourseSnapshot?> GetAsync(
        Guid eventId,
        Guid distanceId,
        CancellationToken cancellationToken = default);
}

public interface IEventCoursesReadQuery
{
    Task<IReadOnlyList<RaceCourseSnapshot>> ListAsync(Guid eventId, CancellationToken cancellationToken = default);
}
