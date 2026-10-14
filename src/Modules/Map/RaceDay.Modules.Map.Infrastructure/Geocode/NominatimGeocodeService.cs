using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using RaceDay.Modules.Map.Application.Geocode;

namespace RaceDay.Modules.Map.Infrastructure.Geocode;

/// <summary>Proxy Nominatim OpenStreetMap — chỉ VN, dùng cho setup map organizer.</summary>
public sealed class NominatimGeocodeService(
    IHttpClientFactory httpClientFactory,
    ILogger<NominatimGeocodeService> logger) : IGeocodeService
{
    public const string HttpClientName = "Nominatim";

    public async Task<GeocodeResponseDto> SearchVietnamAsync(string query, CancellationToken ct = default)
    {
        var q = (query ?? "").Trim();
        if (q.Length < 2)
            return new GeocodeResponseDto([]);

        if (q.Length > 200)
            q = q[..200];

        var client = httpClientFactory.CreateClient(HttpClientName);
        var url =
            "search?format=json&countrycodes=vn&addressdetails=1&limit=8&q="
            + Uri.EscapeDataString(q);

        try
        {
            using var res = await client.GetAsync(url, ct);
            if (!res.IsSuccessStatusCode)
            {
                logger.LogWarning("Nominatim HTTP {Status}", (int)res.StatusCode);
                return new GeocodeResponseDto(
                    [],
                    $"Nominatim HTTP {(int)res.StatusCode}");
            }

            var data = await res.Content.ReadFromJsonAsync<List<NominatimHit>>(cancellationToken: ct)
                       ?? [];

            var results = data.Select(r =>
            {
                GeocodeBBoxDto? bbox = null;
                if (r.BoundingBox is { Count: >= 4 })
                {
                    bbox = new GeocodeBBoxDto(
                        South: ParseDouble(r.BoundingBox[0]),
                        North: ParseDouble(r.BoundingBox[1]),
                        West: ParseDouble(r.BoundingBox[2]),
                        East: ParseDouble(r.BoundingBox[3]));
                }

                return new GeocodeResultDto(
                    Lat: ParseDouble(r.Lat),
                    Lng: ParseDouble(r.Lon),
                    Label: r.DisplayName ?? "",
                    Type: r.Type ?? r.Class ?? "place",
                    Bbox: bbox);
            }).ToList();

            return new GeocodeResponseDto(results);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Nominatim geocode failed for {Query}", q);
            return new GeocodeResponseDto([], "Geocode failed");
        }
    }

    private static double ParseDouble(string? s) =>
        double.TryParse(s, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var v)
            ? v
            : 0;

    private sealed class NominatimHit
    {
        [JsonPropertyName("lat")]
        public string? Lat { get; set; }

        [JsonPropertyName("lon")]
        public string? Lon { get; set; }

        [JsonPropertyName("display_name")]
        public string? DisplayName { get; set; }

        [JsonPropertyName("type")]
        public string? Type { get; set; }

        [JsonPropertyName("class")]
        public string? Class { get; set; }

        [JsonPropertyName("boundingbox")]
        public List<string>? BoundingBox { get; set; }
    }
}
