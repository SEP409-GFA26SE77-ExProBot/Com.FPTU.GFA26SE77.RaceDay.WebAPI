namespace RaceDay.Modules.Event.Application.Certificates;

public sealed record CertificateDataDto(
    Guid RegistrationId,
    string RunnerName,
    string EventName,
    string? Bib,
    string Distance,
    DateOnly RaceDate,
    string Status,
    string FinishTime,
    long ResultSeconds,
    DateTime FinishedAtUtc,
    DateTime? StartedAtUtc,
    double DistanceKm,
    string Pace
);
