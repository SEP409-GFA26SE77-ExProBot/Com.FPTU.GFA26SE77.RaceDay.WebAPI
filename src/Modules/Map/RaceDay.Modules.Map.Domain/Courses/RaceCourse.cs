using RaceDay.Shared.Domain.Abstractions;
using RaceDay.Shared.Domain.Exceptions;

namespace RaceDay.Modules.Map.Domain.Courses;

/// <summary>Course (route + checkpoints) for one event distance — FE CourseConfigDto payload.</summary>
public sealed class RaceCourse : AggregateRoot
{
    public Guid EventId { get; private set; }
    public Guid DistanceId { get; private set; }
    public string Title { get; private set; } = string.Empty;
    public double DistanceKm { get; private set; }
    /// <summary>Full CourseConfig JSON (waypoints, polyline, checkpoints).</summary>
    public string CourseJson { get; private set; } = "{}";
    public Guid UpdatedByUserId { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }

    private RaceCourse()
    {
    }

    public static RaceCourse Create(
        Guid eventId,
        Guid distanceId,
        string title,
        double distanceKm,
        string courseJson,
        Guid updatedByUserId)
    {
        if (eventId == Guid.Empty || distanceId == Guid.Empty)
        {
            throw new DomainException("EventId / DistanceId không hợp lệ.");
        }

        if (string.IsNullOrWhiteSpace(courseJson))
        {
            throw new DomainException("Course payload không được trống.");
        }

        var now = DateTime.UtcNow;
        return new RaceCourse
        {
            EventId = eventId,
            DistanceId = distanceId,
            Title = string.IsNullOrWhiteSpace(title) ? "Cự ly" : title.Trim(),
            DistanceKm = distanceKm < 0 ? 0 : distanceKm,
            CourseJson = courseJson,
            UpdatedByUserId = updatedByUserId,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };
    }

    public void Replace(string title, double distanceKm, string courseJson, Guid updatedByUserId)
    {
        if (string.IsNullOrWhiteSpace(courseJson))
        {
            throw new DomainException("Course payload không được trống.");
        }

        Title = string.IsNullOrWhiteSpace(title) ? Title : title.Trim();
        DistanceKm = distanceKm < 0 ? 0 : distanceKm;
        CourseJson = courseJson;
        UpdatedByUserId = updatedByUserId;
        UpdatedAtUtc = DateTime.UtcNow;
    }
}
