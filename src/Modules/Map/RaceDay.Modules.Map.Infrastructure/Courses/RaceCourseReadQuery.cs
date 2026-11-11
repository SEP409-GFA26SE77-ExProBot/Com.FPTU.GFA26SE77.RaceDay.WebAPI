using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using RaceDay.Modules.Map.Contracts.Courses;
using RaceDay.Modules.Map.Infrastructure.Persistence;

namespace RaceDay.Modules.Map.Infrastructure.Courses;

public sealed class RaceCourseReadQuery(MapDbContext db) : IRaceCourseReadQuery
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public async Task<RaceCourseSnapshot?> GetAsync(
        Guid eventId,
        Guid distanceId,
        CancellationToken cancellationToken = default)
    {
        var course = await db.RaceCourses.AsNoTracking()
            .FirstOrDefaultAsync(row => row.EventId == eventId && row.DistanceId == distanceId, cancellationToken);
        if (course is null)
        {
            return null;
        }

        CourseFile? file = null;
        try
        {
            file = JsonSerializer.Deserialize<CourseFile>(course.CourseJson, Options);
        }
        catch (JsonException)
        {
            file = null;
        }

        var distanceKm = file is { DistanceKm: > 0 } ? file.DistanceKm : course.DistanceKm;
        var polyline = (file?.Polyline ?? []).Select(point => new RaceCoursePoint(point.Lat, point.Lng)).ToList();
        var checkpoints = new List<RaceCourseCheckpoint>();
        var source = file?.Checkpoints ?? [];
        for (var i = 0; i < source.Count; i++)
        {
            var checkpoint = source[i];
            var id = string.IsNullOrWhiteSpace(checkpoint.Id)
                ? $"cp-{i}-{checkpoint.Km.ToString(CultureInfo.InvariantCulture)}"
                : checkpoint.Id;
            checkpoints.Add(new RaceCourseCheckpoint(
                id,
                string.IsNullOrWhiteSpace(checkpoint.Name) ? id : checkpoint.Name,
                checkpoint.Km,
                checkpoint.Lat,
                checkpoint.Lng,
                checkpoint.Type ?? ""));
        }

        var title = string.IsNullOrWhiteSpace(file?.Title) ? course.Title : file!.Title!;
        return new RaceCourseSnapshot(course.EventId, course.DistanceId, title, distanceKm, polyline, checkpoints);
    }

    private sealed record CourseFile(
        string? Title,
        double DistanceKm,
        List<PointFile>? Polyline,
        List<CheckpointFile>? Checkpoints);

    private sealed record PointFile(double Lat, double Lng);

    private sealed record CheckpointFile(string? Id, string? Name, double Km, double Lat, double Lng, string? Type);
}
