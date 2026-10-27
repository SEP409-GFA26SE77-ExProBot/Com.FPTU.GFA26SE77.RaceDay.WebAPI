namespace RaceDay.Modules.Event.Application.Certificates;

public sealed record CertificateDataDto(
    string RunnerName,
    string BibNumber,
    string RaceName,
    string DistanceName,
    double DistanceKm,
    string FinishTime,
    string Pace,
    int? Rank,
    DateTime CompletedAtUtc
);