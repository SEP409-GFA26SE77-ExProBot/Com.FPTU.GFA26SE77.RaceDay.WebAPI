using RaceDay.Shared.Domain.Exceptions;

namespace RaceDay.Modules.Simulation.Domain;

public enum SimulationPhase
{
    Ready,
    Running,
    Paused,
    Finished
}

public enum AthleteMotion
{
    Racing,
    Finished,
    Dnf,
    Dns,
    Dq
}

public sealed record SimulationCoursePoint(double Lat, double Lng);

public sealed record SimulationCourseCheckpoint(
    string Id,
    string Name,
    double Km,
    double Lat,
    double Lng,
    string Type);

public sealed record SimulationAthleteSeed(
    Guid RegistrationId,
    string Bib,
    string FullName,
    string ChipId);

public sealed record SimulationSplit(
    string CheckpointId,
    string Name,
    double Km,
    long SimMicros);

public sealed record SimulationManualOp(
    long Sequence,
    double SimSeconds,
    string Kind,
    string Subject);

public sealed class SimulationAthleteView
{
    public Guid RegistrationId { get; init; }
    public string Bib { get; init; } = "";
    public string FullName { get; init; } = "";
    public string ChipId { get; init; } = "";
    public double BaseKmh { get; init; }
    public double SpeedKmh { get; init; }
    public double CourseKm { get; init; }
    public AthleteMotion Motion { get; init; }
    public bool Sos { get; init; }
    public bool ValidFinish { get; init; }
    public double? FinishSimSeconds { get; init; }
    public IReadOnlyList<SimulationSplit> Splits { get; init; } = [];
}

public sealed class SimulationEngineView
{
    public SimulationPhase Phase { get; init; }
    public double SimulatedSeconds { get; init; }
    public long SimulatedMicros { get; init; }
    public long ResidualMicros { get; init; }
    public IReadOnlyList<SimulationAthleteView> Athletes { get; init; } = [];
    public IReadOnlyList<SimulationManualOp> Log { get; init; } = [];
}

/// <summary>
/// Display rounding for draft ranking. Matches <c>ResultTime.RoundElapsed</c> (AwayFromZero) without referencing Event.
/// </summary>
public static class SimulationClockMath
{
    public static long RoundSeconds(double seconds)
    {
        if (double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds < 0)
        {
            throw new DomainException("Kết quả finish không hợp lệ.");
        }

        return (long)Math.Round(seconds, MidpointRounding.AwayFromZero);
    }

    public static double MicrosToSeconds(long micros) => micros / 1_000_000d;
}

public static class SimulationCourseRules
{
    public static string? Validate(
        double distanceKm,
        IReadOnlyList<SimulationCoursePoint> polyline,
        IReadOnlyList<SimulationCourseCheckpoint> checkpoints)
    {
        if (distanceKm <= 0 || !double.IsFinite(distanceKm))
        {
            return "Cự ly phải lớn hơn 0.";
        }

        if (polyline is null || polyline.Count < 2)
        {
            return "Course cần polyline ít nhất 2 điểm.";
        }

        foreach (var point in polyline)
        {
            if (!double.IsFinite(point.Lat) || !double.IsFinite(point.Lng))
            {
                return "Polyline có toạ độ không hợp lệ.";
            }
        }

        if (checkpoints is null || checkpoints.Count < 2)
        {
            return "Course cần checkpoint xuất phát và đích.";
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var previous = double.NegativeInfinity;
        var starts = 0;
        var finishes = 0;
        for (var i = 0; i < checkpoints.Count; i++)
        {
            var checkpoint = checkpoints[i];
            if (string.IsNullOrWhiteSpace(checkpoint.Id) || !seen.Add(checkpoint.Id))
            {
                return "Checkpoint thiếu id hoặc bị trùng.";
            }

            if (!double.IsFinite(checkpoint.Km) || checkpoint.Km < -1e-6 || checkpoint.Km > distanceKm + 1e-3)
            {
                return $"Checkpoint {checkpoint.Id} nằm ngoài cự ly.";
            }

            if (checkpoint.Km <= previous + 1e-9)
            {
                return "Km checkpoint phải tăng nghiêm ngặt.";
            }

            previous = checkpoint.Km;
            var type = (checkpoint.Type ?? "").Trim();
            if (type.Equals("start", StringComparison.OrdinalIgnoreCase))
            {
                starts++;
            }

            if (type.Equals("finish", StringComparison.OrdinalIgnoreCase))
            {
                finishes++;
            }
        }

        if (starts != 1 || finishes != 1)
        {
            return "Course cần đúng một checkpoint Start và một checkpoint Finish.";
        }

        var first = checkpoints[0];
        var last = checkpoints[^1];
        if (!first.Type.Equals("start", StringComparison.OrdinalIgnoreCase) || first.Km > 1e-3)
        {
            return "Checkpoint đầu phải là Start tại km 0.";
        }

        if (!last.Type.Equals("finish", StringComparison.OrdinalIgnoreCase) || Math.Abs(last.Km - distanceKm) > 1e-3)
        {
            return "Checkpoint cuối phải là Finish tại đúng km cự ly.";
        }

        return null;
    }
}
