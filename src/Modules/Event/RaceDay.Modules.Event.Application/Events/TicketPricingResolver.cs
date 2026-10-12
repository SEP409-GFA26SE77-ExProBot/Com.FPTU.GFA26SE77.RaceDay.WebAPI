using System.Globalization;
using System.Text.Json;

namespace RaceDay.Modules.Event.Application.Events;

/// <summary>
/// Resolve giá vé theo ticketPricing JSON + ngày hiện tại.
/// Ưu tiên phase đang mở không phải standard → standard đang mở → basePrice / distance.Price.
/// </summary>
public static class TicketPricingResolver
{
    public sealed record Resolved(
        decimal Price,
        string? PhaseKey,
        string? PhaseName,
        decimal BasePrice);

    public static Resolved Resolve(
        string? ticketPricingJson,
        string distanceName,
        decimal distanceListPrice,
        DateTime? utcNow = null)
    {
        var fallback = distanceListPrice < 0 ? 0 : distanceListPrice;
        // Phase dateFrom/dateTo theo lịch VN — không dùng UTC thuần (lệch gần nửa đêm).
        var instant = utcNow ?? DateTime.UtcNow;
        var today = DateOnly.FromDateTime(ToVietnamLocal(instant));

        if (string.IsNullOrWhiteSpace(ticketPricingJson) || ticketPricingJson is "{}")
        {
            return new Resolved(fallback, null, null, fallback);
        }

        using var doc = JsonDocument.Parse(ticketPricingJson);
        var root = doc.RootElement;

        if (root.TryGetProperty("byDistanceName", out var byName)
            && byName.ValueKind == JsonValueKind.Object
            && byName.TryGetProperty(distanceName, out var row))
        {
            var basePrice = ReadDecimal(row, "basePrice") ?? fallback;
            var phases = ReadPhasesByDistance(row);
            return Pick(phases, basePrice, today);
        }

        if (root.TryGetProperty("phases", out var legacy) && legacy.ValueKind == JsonValueKind.Array)
        {
            var phases = new List<Phase>();
            var basePrice = fallback;
            foreach (var p in legacy.EnumerateArray())
            {
                var key = ReadString(p, "key") ?? "";
                var name = ReadString(p, "name") ?? key;
                decimal price = 0;
                if (p.TryGetProperty("pricesByDistanceName", out var map)
                    && map.ValueKind == JsonValueKind.Object
                    && map.TryGetProperty(distanceName, out var cell))
                {
                    price = cell.ValueKind == JsonValueKind.Number ? cell.GetDecimal() : 0;
                }

                var standard = IsStandard(key, name);
                if (standard && price > 0)
                {
                    basePrice = price;
                }

                phases.Add(new Phase(
                    key,
                    string.IsNullOrWhiteSpace(name) ? key : name,
                    ReadDate(p, "dateFrom"),
                    ReadDate(p, "dateTo"),
                    price,
                    standard));
            }

            return Pick(phases, basePrice, today);
        }

        return new Resolved(fallback, null, null, fallback);
    }

    private static Resolved Pick(IReadOnlyList<Phase> phases, decimal basePrice, DateOnly today)
    {
        var active = phases
            .Where(p => p.Price > 0 && InRange(today, p.From, p.To))
            .ToList();

        var promo = active.FirstOrDefault(p => !p.IsStandard);
        if (promo is not null)
        {
            return new Resolved(promo.Price, promo.Key, promo.Name, basePrice);
        }

        var standard = active.FirstOrDefault(p => p.IsStandard);
        if (standard is not null)
        {
            return new Resolved(standard.Price, standard.Key, standard.Name, basePrice);
        }

        return new Resolved(basePrice, null, null, basePrice);
    }

    private static List<Phase> ReadPhasesByDistance(JsonElement row)
    {
        var list = new List<Phase>();
        if (!row.TryGetProperty("phases", out var arr) || arr.ValueKind != JsonValueKind.Array)
        {
            return list;
        }

        var i = 0;
        foreach (var p in arr.EnumerateArray())
        {
            var key = ReadString(p, "key") ?? $"phase-{i}";
            var name = ReadString(p, "name") ?? key;
            list.Add(new Phase(
                key,
                name,
                ReadDate(p, "dateFrom"),
                ReadDate(p, "dateTo"),
                ReadDecimal(p, "price") ?? 0,
                IsStandard(key, name)));
            i++;
        }

        return list;
    }

    private static bool IsStandard(string key, string name)
    {
        var blob = $"{key} {name}".ToLowerInvariant();
        return key.Equals("regular", StringComparison.OrdinalIgnoreCase)
               || key.Equals("standard", StringComparison.OrdinalIgnoreCase)
               || key.Equals("base", StringComparison.OrdinalIgnoreCase)
               || blob.Contains("regular", StringComparison.Ordinal)
               || blob.Contains("standard", StringComparison.Ordinal)
               || blob.Contains("giá gốc", StringComparison.Ordinal)
               || blob.Contains("gia goc", StringComparison.Ordinal);
    }

    private static bool InRange(DateOnly today, DateOnly? from, DateOnly? to)
    {
        if (from is null && to is null) return false;
        if (from is not null && today < from.Value) return false;
        if (to is not null && today > to.Value) return false;
        return true;
    }

    private static DateOnly? ReadDate(JsonElement el, string name)
    {
        if (!el.TryGetProperty(name, out var p) || p.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        var s = p.GetString();
        if (string.IsNullOrWhiteSpace(s)) return null;
        if (DateOnly.TryParse(s.AsSpan(0, Math.Min(10, s.Length)), CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
        {
            return d;
        }

        return null;
    }

    private static decimal? ReadDecimal(JsonElement el, string name)
    {
        if (!el.TryGetProperty(name, out var p)) return null;
        return p.ValueKind == JsonValueKind.Number ? p.GetDecimal() : null;
    }

    private static string? ReadString(JsonElement el, string name)
    {
        if (!el.TryGetProperty(name, out var p) || p.ValueKind != JsonValueKind.String) return null;
        return p.GetString();
    }

    private sealed record Phase(
        string Key,
        string Name,
        DateOnly? From,
        DateOnly? To,
        decimal Price,
        bool IsStandard);

    private static DateTime ToVietnamLocal(DateTime utcOrUnspecified)
    {
        var utc = utcOrUnspecified.Kind switch
        {
            DateTimeKind.Utc => utcOrUnspecified,
            DateTimeKind.Local => utcOrUnspecified.ToUniversalTime(),
            _ => DateTime.SpecifyKind(utcOrUnspecified, DateTimeKind.Utc)
        };

        try
        {
            var tz = TimeZoneInfo.FindSystemTimeZoneById("Asia/Ho_Chi_Minh");
            return TimeZoneInfo.ConvertTimeFromUtc(utc, tz);
        }
        catch (TimeZoneNotFoundException)
        {
            try
            {
                var tz = TimeZoneInfo.FindSystemTimeZoneById("SE Asia Standard Time");
                return TimeZoneInfo.ConvertTimeFromUtc(utc, tz);
            }
            catch (TimeZoneNotFoundException)
            {
                return utc.AddHours(7);
            }
        }
    }
}
