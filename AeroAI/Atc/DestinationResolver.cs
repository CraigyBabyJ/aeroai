using System;
using System.Linq;
using System.Text.RegularExpressions;

namespace AeroAI.Atc;

public static class DestinationResolver
{
    public static bool Matches(string pilotText, FlightContext context)
    {
        if (context == null)
            return false;

        var plannedIcao = context.DestinationIcao?.Trim().ToUpperInvariant();
        var plannedName = context.DestinationName?.Trim();
        var normalizedPilot = pilotText?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(normalizedPilot))
            return false;

        // Affirm/Yes/Correct without content is acceptable if we have a pending confirm.
        var lower = normalizedPilot.ToLowerInvariant();
        if (lower == "affirm" || lower == "affirmative" || lower == "yes" || lower == "correct" || lower == "confirmed" || lower == "roger")
            return true;

        // Direct ICAO token
        if (!string.IsNullOrWhiteSpace(plannedIcao) && Regex.IsMatch(normalizedPilot, $@"\b{Regex.Escape(plannedIcao)}\b", RegexOptions.IgnoreCase))
            return true;

        // IATA token (if provided in destination name: pattern like KJFK/JFK)
        if (!string.IsNullOrWhiteSpace(plannedName))
        {
            var iata = ExtractIata(plannedName);
            if (!string.IsNullOrWhiteSpace(iata) && Regex.IsMatch(normalizedPilot, $@"\b{Regex.Escape(iata)}\b", RegexOptions.IgnoreCase))
                return true;
        }

        // Name-based fuzzy match
        if (!string.IsNullOrWhiteSpace(plannedName) && NamesClose(normalizedPilot, plannedName))
            return true;

        return false;
    }

    private static bool NamesClose(string spoken, string planned)
    {
        var spokenTokens = ExtractTokens(spoken);
        var plannedTokens = ExtractTokens(planned);
        if (spokenTokens.Count == 0 || plannedTokens.Count == 0)
            return false;

        // Require at least one near-match token
        foreach (var s in spokenTokens)
        {
            foreach (var p in plannedTokens)
            {
                if (TokenClose(s, p))
                    return true;
            }
        }

        return false;
    }

    private static bool TokenClose(string a, string b)
    {
        if (a.Equals(b, StringComparison.OrdinalIgnoreCase))
            return true;

        if (Math.Abs(a.Length - b.Length) > 2)
            return false;

        return LevenshteinDistance(a.ToLowerInvariant(), b.ToLowerInvariant()) <= 2;
    }

    private static int LevenshteinDistance(string a, string b)
    {
        int[,] d = new int[a.Length + 1, b.Length + 1];
        for (int i = 0; i <= a.Length; i++) d[i, 0] = i;
        for (int j = 0; j <= b.Length; j++) d[0, j] = j;

        for (int i = 1; i <= a.Length; i++)
        {
            for (int j = 1; j <= b.Length; j++)
            {
                int cost = (a[i - 1] == b[j - 1]) ? 0 : 1;
                d[i, j] = Math.Min(
                    Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1),
                    d[i - 1, j - 1] + cost);
            }
        }
        return d[a.Length, b.Length];
    }

    private static string? ExtractIata(string name)
    {
        // crude: look for (IATA: XXX) patterns or three-letter uppercase tokens in parens
        var match = Regex.Match(name, @"\b([A-Z]{3})\b");
        if (match.Success)
            return match.Groups[1].Value;
        return null;
    }

    private static System.Collections.Generic.List<string> ExtractTokens(string value)
    {
        return Regex.Matches(value, "[A-Za-z]+")
            .Select(m => m.Value.ToLowerInvariant())
            .Where(t => t.Length >= 3)
            .ToList();
    }
}
