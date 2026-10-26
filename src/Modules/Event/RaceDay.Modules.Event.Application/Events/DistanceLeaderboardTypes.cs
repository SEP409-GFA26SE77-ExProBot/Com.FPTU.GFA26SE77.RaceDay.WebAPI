namespace RaceDay.Modules.Event.Application.Events;

/// <summary>
/// Mã type cự ly cho chip BXH (All-3KM, All-HM…). Không phải mặt đường road/trail.
/// Map route vẫn dùng <c>DistanceKm</c>.
/// </summary>
public static class DistanceLeaderboardTypes
{
    public const string Other = "OTHER";

    /// <summary>Dropdown phổ biến — cự ly lẻ dùng OTHER + DistanceKm.</summary>
    public static readonly IReadOnlyList<(string Code, double SuggestedKm, string Label)> Catalog =
    [
        ("3KM", 3, "3 km"),
        ("5KM", 5, "5 km"),
        ("10KM", 10, "10 km"),
        ("15KM", 15, "15 km"),
        ("HM", 21.0975, "Half Marathon (HM)"),
        ("M", 42.195, "Marathon (M)"),
        (Other, 0, "Khác"),
    ];

    private static readonly HashSet<string> Known = new(
        Catalog.Select(x => x.Code),
        StringComparer.OrdinalIgnoreCase);

    // Không gồm "other" — trùng mã chip BXH OTHER.
    private static readonly HashSet<string> SurfaceLegacy = new(StringComparer.OrdinalIgnoreCase)
    {
        "road", "trail", "ultra", "track", "mixed"
    };

    public static bool IsKnown(string? code)
        => !string.IsNullOrWhiteSpace(code) && Known.Contains(code.Trim());

    /// <summary>Chuẩn hoá alias (5K→5KM, 21K→HM, half→HM…).</summary>
    public static string Normalize(string? raw, double distanceKm = 0)
    {
        if (string.IsNullOrWhiteSpace(raw) || SurfaceLegacy.Contains(raw.Trim()))
            return SuggestFromKm(distanceKm);

        var t = raw.Trim().ToUpperInvariant()
            .Replace(" ", "")
            .Replace("-", "");

        t = t switch
        {
            "5K" => "5KM",
            "10K" => "10KM",
            "15K" => "15KM",
            "20K" => "20KM",
            "21K" or "21KM" or "HALF" or "HALFMARATHON" or "HALF_MARATHON" => "HM",
            "42K" or "42KM" or "MARATHON" or "FULL" or "FULLMARATHON" => "M",
            "1K" => "1KM",
            "2K" => "2KM",
            "3K" => "3KM",
            "50K" => "50KM",
            "100K" => "100KM",
            "KHAC" or "OTHER" => Other,
            _ => t
        };

        if (Known.Contains(t))
            return t;

        // "3KM" already; bare number "3" → 3KM
        if (double.TryParse(t.TrimEnd('K', 'M'), System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out var n)
            && n > 0)
        {
            var asKm = $"{n:0.###}KM".Replace(".000", "").Replace(",000", "");
            // prefer catalog match via Suggest
            return SuggestFromKm(n);
        }

        return SuggestFromKm(distanceKm);
    }

    public static string SuggestFromKm(double km)
    {
        if (km <= 0)
            return Other;

        if (Math.Abs(km - 21.0975) < 0.35 || Math.Abs(km - 21.1) < 0.2 || Math.Abs(km - 21) < 0.15)
            return "HM";
        if (Math.Abs(km - 42.195) < 0.4 || Math.Abs(km - 42.2) < 0.25 || Math.Abs(km - 42) < 0.2)
            return "M";

        string? best = null;
        var bestDelta = double.MaxValue;
        foreach (var (code, suggested, _) in Catalog)
        {
            if (code == Other || suggested <= 0)
                continue;
            var delta = Math.Abs(km - suggested);
            // cho phép lệch ~8% hoặc 0.6km
            var tol = Math.Max(0.6, suggested * 0.08);
            if (delta <= tol && delta < bestDelta)
            {
                bestDelta = delta;
                best = code;
            }
        }

        return best ?? Other;
    }

    public static double? SuggestedKm(string? code)
    {
        var n = Normalize(code);
        var hit = Catalog.FirstOrDefault(x => x.Code.Equals(n, StringComparison.OrdinalIgnoreCase));
        return hit.SuggestedKm > 0 ? hit.SuggestedKm : null;
    }

    /// <summary>
    /// Gợi ý prefix BIB theo type — chỉ số km (HM→21, M→42). OTHER → trống.
    /// </summary>
    public static string SuggestBibPrefix(string? type)
    {
        var n = Normalize(type);
        return n switch
        {
            "3KM" => "3",
            "5KM" => "5",
            "10KM" => "10",
            "15KM" => "15",
            "HM" => "21",
            "M" => "42",
            _ => string.Empty,
        };
    }
}
