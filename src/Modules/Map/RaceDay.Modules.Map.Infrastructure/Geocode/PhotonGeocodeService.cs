using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using RaceDay.Modules.Map.Application.Geocode;

namespace RaceDay.Modules.Map.Infrastructure.Geocode;

/// <summary>
/// Geocode VN qua Photon (Komoot). Nominatim OSM DNS thường fail ở VN.
/// </summary>
public sealed class PhotonGeocodeService(
    IHttpClientFactory httpClientFactory,
    ILogger<PhotonGeocodeService> logger) : IGeocodeService
{
    public const string HttpClientName = "Photon";

    /// <summary>bbox minLon,minLat,maxLon,maxLat — gần đúng lãnh thổ VN.</summary>
    private const string VietnamBbox = "102.1,8.4,109.5,23.4";

    public async Task<GeocodeResponseDto> SearchVietnamAsync(string query, CancellationToken ct = default)
    {
        var q = (query ?? "").Trim();
        if (q.Length < 2)
            return new GeocodeResponseDto([]);

        if (q.Length > 200)
            q = q[..200];

        var client = httpClientFactory.CreateClient(HttpClientName);
        var url =
            "api/?limit=8&lang=en&bbox="
            + VietnamBbox
            + "&q="
            + Uri.EscapeDataString(q);

        try
        {
            using var res = await client.GetAsync(url, ct);
            if (!res.IsSuccessStatusCode)
            {
                logger.LogWarning("Photon HTTP {Status}", (int)res.StatusCode);
                return new GeocodeResponseDto([], $"Photon HTTP {(int)res.StatusCode}");
            }

            var data = await res.Content.ReadFromJsonAsync<PhotonResponse>(cancellationToken: ct);
            var features = data?.Features ?? [];

            var results = features
                .Where(f =>
                    f.Geometry?.Coordinates is { Count: >= 2 }
                    && (string.IsNullOrEmpty(f.Properties?.CountryCode)
                        || string.Equals(f.Properties!.CountryCode, "VN", StringComparison.OrdinalIgnoreCase)))
                .Select(f =>
                {
                    var coords = f.Geometry!.Coordinates!;
                    var lon = coords[0];
                    var lat = coords[1];
                    var p = f.Properties;
                    var label = BuildLabel(p);
                    GeocodeBBoxDto? bbox = null;
                    if (p?.Extent is { Count: >= 4 })
                    {
                        // Photon extent: minLon, maxLat, maxLon, minLat
                        bbox = new GeocodeBBoxDto(
                            South: p.Extent[3],
                            North: p.Extent[1],
                            West: p.Extent[0],
                            East: p.Extent[2]);
                    }

                    return new GeocodeResultDto(
                        Lat: lat,
                        Lng: lon,
                        Label: label,
                        Type: p?.OsmValue ?? p?.Type ?? "place",
                        Bbox: bbox);
                })
                .Where(r => !string.IsNullOrWhiteSpace(r.Label))
                .Take(8)
                .ToList();

            return new GeocodeResponseDto(results);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Photon geocode failed for {Query}", q);
            return new GeocodeResponseDto([], "Geocode failed");
        }
    }

    private static string BuildLabel(PhotonProps? p)
    {
        if (p is null) return "";
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(p.Name)) parts.Add(p.Name!);
        if (!string.IsNullOrWhiteSpace(p.Street)) parts.Add(p.Street!);
        if (!string.IsNullOrWhiteSpace(p.District)) parts.Add(p.District!);
        if (!string.IsNullOrWhiteSpace(p.City)) parts.Add(p.City!);
        if (!string.IsNullOrWhiteSpace(p.State) && !parts.Contains(p.State!)) parts.Add(p.State!);
        if (!string.IsNullOrWhiteSpace(p.Country)) parts.Add(p.Country!);
        return string.Join(", ", parts);
    }

    private sealed class PhotonResponse
    {
        [JsonPropertyName("features")]
        public List<PhotonFeature>? Features { get; set; }
    }

    private sealed class PhotonFeature
    {
        [JsonPropertyName("geometry")]
        public PhotonGeometry? Geometry { get; set; }

        [JsonPropertyName("properties")]
        public PhotonProps? Properties { get; set; }
    }

    private sealed class PhotonGeometry
    {
        [JsonPropertyName("coordinates")]
        public List<double>? Coordinates { get; set; }
    }

    private sealed class PhotonProps
    {
        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("street")]
        public string? Street { get; set; }

        [JsonPropertyName("district")]
        public string? District { get; set; }

        [JsonPropertyName("city")]
        public string? City { get; set; }

        [JsonPropertyName("state")]
        public string? State { get; set; }

        [JsonPropertyName("country")]
        public string? Country { get; set; }

        [JsonPropertyName("countrycode")]
        public string? CountryCode { get; set; }

        [JsonPropertyName("osm_value")]
        public string? OsmValue { get; set; }

        [JsonPropertyName("type")]
        public string? Type { get; set; }

        [JsonPropertyName("extent")]
        public List<double>? Extent { get; set; }
    }
}
