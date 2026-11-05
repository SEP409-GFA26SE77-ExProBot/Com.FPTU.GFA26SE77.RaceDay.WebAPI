using System.Text.Json.Serialization;

namespace RaceDay.Modules.Event.Application.Certificates;

/// <summary>
/// Dữ liệu chứng nhận cho FE JSON và QuestPDF.
/// Alias properties phục vụ <c>ICertificatePdfService</c> (RaceName/BibNumber/…) — không serialize ra JSON.
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
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? Rank = null)
{
    [JsonIgnore] public string RaceName => EventName;
    [JsonIgnore] public string BibNumber => string.IsNullOrWhiteSpace(Bib) ? "N/A" : Bib;
    [JsonIgnore] public string DistanceName => Distance;
    [JsonIgnore] public DateTime CompletedAtUtc => FinishedAtUtc;
}
