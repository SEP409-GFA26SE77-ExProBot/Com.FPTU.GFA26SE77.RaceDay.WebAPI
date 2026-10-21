using System.Text.RegularExpressions;

namespace RaceDay.Modules.Photo.Application.Photos;

/// <summary>
/// Maps OCR bib tokens onto Event roster forms (mirrors EventDistance.FormatBib:
/// prefix + sequence D4, e.g. A0020 / 210020 / 0020).
/// </summary>
internal static class BibRosterResolver
{
    public static string? Resolve(string? candidate, IReadOnlySet<string> roster)
    {
        var c = NormalizeAlnum(candidate);
        if (c.Length == 0)
        {
            return null;
        }

        if (roster.Count == 0)
        {
            return c;
        }

        var normToOrig = new Dictionary<string, string>(StringComparer.Ordinal);
        var bySeq = new Dictionary<int, List<string>>();
        foreach (var raw in roster)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                continue;
            }

            var n = NormalizeAlnum(raw);
            if (n.Length == 0)
            {
                continue;
            }

            normToOrig.TryAdd(n, raw.Trim().ToUpperInvariant());
            if (!TryParseWebApiBib(n, out _, out var seq))
            {
                continue;
            }

            if (!bySeq.TryGetValue(seq, out var list))
            {
                list = [];
                bySeq[seq] = list;
            }

            var orig = normToOrig[n];
            if (!list.Contains(orig, StringComparer.OrdinalIgnoreCase))
            {
                list.Add(orig);
            }
        }

        if (normToOrig.TryGetValue(c, out var exactHit))
        {
            return exactHit;
        }

        if (c.All(char.IsDigit) && int.TryParse(c, out var digitSeq) && digitSeq is >= 1 and <= 9999)
        {
            if (bySeq.TryGetValue(digitSeq, out var hits) && hits.Count == 1)
            {
                return hits[0];
            }

            return null;
        }

        if (TryParseWebApiBib(c, out var candPref, out var candSeq))
        {
            var formatted = FormatBib(candPref, candSeq);
            if (normToOrig.TryGetValue(NormalizeAlnum(formatted), out var formattedHit))
            {
                return formattedHit;
            }

            if (candPref.Length == 0
                && bySeq.TryGetValue(candSeq, out var hits)
                && hits.Count == 1)
            {
                return hits[0];
            }
        }

        foreach (var orig in normToOrig.Values)
        {
            if (!TryParseWebApiBib(NormalizeAlnum(orig), out var pref, out _))
            {
                continue;
            }

            if (pref.Length == 0 || !c.StartsWith(pref, StringComparison.Ordinal))
            {
                continue;
            }

            var rest = c[pref.Length..];
            if (!int.TryParse(rest, out var seq) || seq is < 1 or > 9999)
            {
                continue;
            }

            var formatted = FormatBib(pref, seq);
            if (normToOrig.TryGetValue(NormalizeAlnum(formatted), out var hit))
            {
                return hit;
            }
        }

        return null;
    }

    private static string NormalizeAlnum(string? raw) =>
        Regex.Replace((raw ?? string.Empty).ToUpperInvariant(), "[^A-Z0-9]", string.Empty);

    private static bool TryParseWebApiBib(string bib, out string prefix, out int sequence)
    {
        prefix = string.Empty;
        sequence = 0;
        if (bib.Length >= 4 && bib[^4..].All(char.IsDigit)
            && int.TryParse(bib[^4..], out sequence)
            && sequence is >= 1 and <= 9999)
        {
            prefix = bib[..^4];
            return true;
        }

        if (bib.All(char.IsDigit)
            && int.TryParse(bib, out sequence)
            && sequence is >= 1 and <= 9999)
        {
            prefix = string.Empty;
            return true;
        }

        return false;
    }

    private static string FormatBib(string prefix, int sequence)
    {
        var num = sequence.ToString("D4");
        return string.IsNullOrEmpty(prefix) ? num : $"{prefix}{num}";
    }
}
