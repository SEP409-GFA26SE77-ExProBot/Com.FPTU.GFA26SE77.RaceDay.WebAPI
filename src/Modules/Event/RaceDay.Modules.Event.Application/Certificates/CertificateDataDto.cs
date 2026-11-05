namespace RaceDay.Modules.Event.Application.Certificates;

/// <summary>
/// Dữ liệu chứng nhận cho FE JSON và QuestPDF.
/// Alias properties phục vụ <c>ICertificatePdfService</c> (RaceName/BibNumber/…).
/// </summary>
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
    string Pace,
    int? Rank = null)
{
    public string RaceName => EventName;
    public string BibNumber => string.IsNullOrWhiteSpace(Bib) ? "N/A" : Bib;
    public string DistanceName => Distance;
    public DateTime CompletedAtUtc => FinishedAtUtc;
}
