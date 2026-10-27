using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using RaceDay.Modules.Photo.Application.Abstractions;

namespace RaceDay.Modules.Photo.Infrastructure.Ocr;

public sealed class OcrClient(
    HttpClient http,
    IConfiguration configuration,
    ILogger<OcrClient> logger) : IOcrClient
{
    public bool IsConfigured
    {
        get
        {
            var baseUrl = configuration["Photo:Ocr:BaseUrl"];
            var key = configuration["Photo:Ocr:ServiceKey"];
            return !string.IsNullOrWhiteSpace(baseUrl) && !string.IsNullOrWhiteSpace(key);
        }
    }

    public async Task<OcrEnqueueResult> EnqueueJobAsync(
        Guid jobId,
        Guid eventId,
        Guid photoId,
        string imageUrl,
        string bibRosterVersion,
        IReadOnlyList<string>? rosterBibs,
        CancellationToken cancellationToken = default)
    {
        if (!IsConfigured)
        {
            logger.LogDebug("OCR client not configured — job {JobId} stays queued locally.", jobId);
            return new OcrEnqueueResult(jobId.ToString("N"));
        }

        var payload = new OcrJobRequest(
            jobId.ToString("D"),
            eventId.ToString("D"),
            photoId.ToString("D"),
            imageUrl,
            bibRosterVersion,
            new OcrJobOptions(
                ParseDouble(configuration["Photo:Ocr:ThresholdAuto"], 0.85),
                ParseDouble(configuration["Photo:Ocr:ThresholdReviewMin"], 0.50),
                false),
            rosterBibs);

        using var request = new HttpRequestMessage(HttpMethod.Post, "v1/ocr/jobs")
        {
            Content = JsonContent.Create(payload)
        };
        request.Headers.TryAddWithoutValidation(
            "X-Service-Key",
            configuration["Photo:Ocr:ServiceKey"]);

        using var response = await http.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"OCR enqueue failed ({(int)response.StatusCode}): {body}");
        }

        OcrJobHttpResponse? parsed = null;
        try
        {
            parsed = System.Text.Json.JsonSerializer.Deserialize<OcrJobHttpResponse>(body);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "OCR response JSON parse failed for job {JobId}; body={Body}", jobId, body);
        }

        var remoteId = parsed?.JobId ?? jobId.ToString("N");
        if (parsed?.Result is null)
        {
            return new OcrEnqueueResult(remoteId);
        }

        var r = parsed.Result;
        var tags = (r.Tags ?? [])
            .Select(t => new OcrEnqueueTag(
                t.Bib ?? string.Empty,
                t.Confidence,
                t.Bbox))
            .Where(t => t.Bib.Length > 0)
            .ToList();

        return new OcrEnqueueResult(
            remoteId,
            r.Status,
            r.DuplicateOf,
            tags,
            r.Reasons,
            HasInlineResult: true);
    }

    private static double ParseDouble(string? value, double fallback)
        => double.TryParse(value, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var d)
            ? d
            : fallback;

    private sealed record OcrJobRequest(
        [property: JsonPropertyName("job_id")] string JobId,
        [property: JsonPropertyName("event_id")] string EventId,
        [property: JsonPropertyName("photo_id")] string PhotoId,
        [property: JsonPropertyName("image_url")] string ImageUrl,
        [property: JsonPropertyName("bib_roster_version")] string BibRosterVersion,
        [property: JsonPropertyName("options")] OcrJobOptions Options,
        [property: JsonPropertyName("roster_bibs")] IReadOnlyList<string>? RosterBibs);

    private sealed record OcrJobOptions(
        [property: JsonPropertyName("threshold_auto")] double ThresholdAuto,
        [property: JsonPropertyName("threshold_review_min")] double ThresholdReviewMin,
        [property: JsonPropertyName("force")] bool Force);

    private sealed record OcrJobHttpResponse(
        [property: JsonPropertyName("job_id")] string? JobId,
        [property: JsonPropertyName("status")] string? Status,
        [property: JsonPropertyName("result")] OcrJobHttpResult? Result);

    private sealed record OcrJobHttpResult(
        [property: JsonPropertyName("status")] string? Status,
        [property: JsonPropertyName("duplicate_of")] string? DuplicateOf,
        [property: JsonPropertyName("tags")] IReadOnlyList<OcrJobHttpTag>? Tags,
        [property: JsonPropertyName("reasons")] IReadOnlyList<string>? Reasons);

    private sealed record OcrJobHttpTag(
        [property: JsonPropertyName("bib")] string? Bib,
        [property: JsonPropertyName("confidence")] double Confidence,
        [property: JsonPropertyName("bbox")] IReadOnlyList<double>? Bbox);
}
