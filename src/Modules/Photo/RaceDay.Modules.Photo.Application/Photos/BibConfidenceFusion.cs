using System.Text.RegularExpressions;

namespace RaceDay.Modules.Photo.Application.Photos;

/// <summary>
/// Strongest roster-aware confidence:
/// final = 0.7 × model + 0.3 × match − penalty.
/// Exact/full token → Matched path; fuzzy/partial → Review; weak → drop.
/// Không đoán số bị che thành full bib (partial chỉ review nếu đủ giống).
/// </summary>
public static class BibConfidenceFusion
{
    public const double WeightModel = 0.7;
    public const double WeightMatch = 0.3;
    public const double MatchAutoMin = 0.95;
    public const double MatchReviewMin = 0.70;
    public const double AmbiguousPenalty = 0.08;
    public const double AmbiguousDelta = 0.05;

    public readonly record struct MatchHit(
        string RosterBib,
        double MatchScore,
        bool Ambiguous);

    /// <summary>
    /// Soft ceiling: roster match xác nhận BIB, không được đẩy visual certainty lên ~99%.
    /// Paddle hay overconfident trên ROI bị che một phần.
    /// </summary>
    public const double ReportSoftCap = 0.95;
    public const double MatchBoostMax = 0.04;

    public static double Fuse(double modelConfidence, double matchScore, bool ambiguous)
    {
        var model = Math.Clamp(modelConfidence, 0, 1);
        if (model >= 0.999)
        {
            model = 0.95;
        }

        var match = Math.Clamp(matchScore, 0, 1);
        var final = WeightModel * model + WeightMatch * match
                    - (ambiguous ? AmbiguousPenalty : 0);
        // Match chỉ được nâng tối đa +4pp so với model — tránh 0.98 model + exact → 0.99
        final = Math.Min(final, model + MatchBoostMax);
        return Math.Clamp(Math.Round(final, 4), 0, ReportSoftCap);
    }

    /// <summary>
    /// Best roster bib for an OCR token, with continuous match score.
    /// </summary>
    public static MatchHit? FindBest(string? ocrToken, IReadOnlySet<string> roster)
    {
        var cand = NormalizeAlnum(ocrToken);
        if (cand.Length == 0 || roster.Count == 0)
        {
            return null;
        }

        var scored = new List<(string Orig, double Score)>();
        foreach (var raw in roster)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                continue;
            }

            var orig = raw.Trim().ToUpperInvariant();
            var norm = NormalizeAlnum(orig);
            if (norm.Length == 0)
            {
                continue;
            }

            scored.Add((orig, Similarity(cand, norm)));
        }

        if (scored.Count == 0)
        {
            return null;
        }

        scored.Sort((a, b) => b.Score.CompareTo(a.Score));
        var best = scored[0];
        if (best.Score < MatchReviewMin)
        {
            return null;
        }

        var ambiguous = false;
        if (scored.Count > 1
            && scored[1].Score >= MatchReviewMin
            && best.Score - scored[1].Score < AmbiguousDelta
            && !string.Equals(scored[1].Orig, best.Orig, StringComparison.OrdinalIgnoreCase))
        {
            ambiguous = true;
        }

        // Exact / full-strength resolve bumps to 1.0 (auto-eligible on match axis).
        var resolved = BibRosterResolver.Resolve(cand, roster);
        if (resolved is not null
            && string.Equals(resolved, best.Orig, StringComparison.OrdinalIgnoreCase)
            && IsFullStrengthToken(cand, NormalizeAlnum(resolved)))
        {
            return new MatchHit(resolved, 1.0, ambiguous);
        }

        return new MatchHit(best.Orig, Math.Round(best.Score, 4), ambiguous);
    }

    /// <summary>
    /// Exact equality or complete FormatBib-shaped token — not a short fragment like "0001"/"992".
    /// </summary>
    internal static bool IsFullStrengthToken(string candNorm, string rosterNorm)
    {
        if (candNorm.Equals(rosterNorm, StringComparison.Ordinal))
        {
            return true;
        }

        // Candidate must be at least as informative as D4 / full roster token.
        if (candNorm.Length >= rosterNorm.Length)
        {
            return true;
        }

        if (candNorm.Length >= 5 && rosterNorm.EndsWith(candNorm, StringComparison.Ordinal))
        {
            return true;
        }

        return false;
    }

    internal static double Similarity(string a, string b)
    {
        if (a.Length == 0 || b.Length == 0)
        {
            return 0;
        }

        if (a.Equals(b, StringComparison.Ordinal))
        {
            return 1.0;
        }

        var maxLen = Math.Max(a.Length, b.Length);
        var levScore = 1.0 - (double)Levenshtein(a, b) / maxLen;

        // Partial OCR: shorter token is suffix/prefix of roster (khuất một phần).
        double fragScore = 0;
        if (a.Length >= 3 && b.EndsWith(a, StringComparison.Ordinal))
        {
            fragScore = (double)a.Length / b.Length;
        }
        else if (b.Length >= 3 && a.EndsWith(b, StringComparison.Ordinal))
        {
            fragScore = (double)b.Length / a.Length;
        }
        else if (a.Length >= 3 && b.StartsWith(a, StringComparison.Ordinal))
        {
            fragScore = (double)a.Length / b.Length * 0.95;
        }

        return Math.Clamp(Math.Max(levScore, fragScore), 0, 1);
    }

    private static int Levenshtein(string a, string b)
    {
        var n = a.Length;
        var m = b.Length;
        var d = new int[n + 1, m + 1];
        for (var i = 0; i <= n; i++)
        {
            d[i, 0] = i;
        }

        for (var j = 0; j <= m; j++)
        {
            d[0, j] = j;
        }

        for (var i = 1; i <= n; i++)
        {
            for (var j = 1; j <= m; j++)
            {
                var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                d[i, j] = Math.Min(
                    Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1),
                    d[i - 1, j - 1] + cost);
            }
        }

        return d[n, m];
    }

    private static string NormalizeAlnum(string? raw) =>
        Regex.Replace((raw ?? string.Empty).ToUpperInvariant(), "[^A-Z0-9]", string.Empty);
}
