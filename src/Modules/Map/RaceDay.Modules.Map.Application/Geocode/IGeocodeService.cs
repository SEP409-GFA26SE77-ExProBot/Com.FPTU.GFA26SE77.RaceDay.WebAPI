namespace RaceDay.Modules.Map.Application.Geocode;

public interface IGeocodeService
{
    Task<GeocodeResponseDto> SearchVietnamAsync(string query, CancellationToken ct = default);
}
