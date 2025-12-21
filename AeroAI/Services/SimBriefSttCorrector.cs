using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using AeroAI.Atc;

namespace AeroAI.Services;

public sealed class SimBriefSttCorrector
{
    private const int MaxWindowTokens = 4;
    private const double DefaultThreshold = 0.90;
    private const double LongPhraseThreshold = 0.88;
    private const double ShortThreshold = 0.94;
    private const double IcaoThreshold = 0.96;
    private const double CityThreshold = 0.92;

    private static readonly Regex TokenRegex = new("[A-Za-z0-9]+", RegexOptions.Compiled);
    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "international",
        "airport",
        "airfield",
        "aerodrome",
        "field",
        "intl"
    };

    private static readonly Dictionary<string, string> LetterAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        { "a", "a" },
        { "alpha", "a" },
        { "alfa", "a" },
        { "b", "b" },
        { "bee", "b" },
        { "be", "b" },
        { "c", "c" },
        { "cee", "c" },
        { "see", "c" },
        { "sea", "c" },
        { "d", "d" },
        { "dee", "d" },
        { "e", "e" },
        { "f", "f" },
        { "eff", "f" },
        { "g", "g" },
        { "h", "h" },
        { "i", "i" },
        { "j", "j" },
        { "jay", "j" },
        { "juliet", "j" },
        { "juliett", "j" },
        { "k", "k" },
        { "kay", "k" },
        { "l", "l" },
        { "el", "l" },
        { "ell", "l" },
        { "m", "m" },
        { "em", "m" },
        { "n", "n" },
        { "en", "n" },
        { "o", "o" },
        { "oh", "o" },
        { "p", "p" },
        { "pea", "p" },
        { "pee", "p" },
        { "q", "q" },
        { "cue", "q" },
        { "queue", "q" },
        { "r", "r" },
        { "are", "r" },
        { "s", "s" },
        { "t", "t" },
        { "tee", "t" },
        { "u", "u" },
        { "you", "u" },
        { "v", "v" },
        { "w", "w" },
        { "x", "x" },
        { "ex", "x" },
        { "y", "y" },
        { "why", "y" },
        { "z", "z" },
        { "zee", "z" },
        { "zed", "z" }
    };

    private readonly FlightContext _flight;
    private readonly Action<string>? _onDebug;
    private readonly bool _verbose;

    public SimBriefSttCorrector(FlightContext flight, Action<string>? onDebug = null, bool verbose = false)
    {
        _flight = flight ?? throw new ArgumentNullException(nameof(flight));
        _onDebug = onDebug;
        _verbose = verbose;
    }

    public static string Apply(string transcript, FlightContext? flight, Action<string>? onDebug = null, bool verbose = false)
    {
        if (flight == null)
            return transcript;
        return new SimBriefSttCorrector(flight, onDebug, verbose).Apply(transcript);
    }

    public string Apply(string transcript)
    {
        if (string.IsNullOrWhiteSpace(transcript))
            return transcript;

        try
        {
            var aliases = BuildAliases(_flight);
            if (aliases.Count == 0)
                return transcript;

            var tokens = Tokenize(transcript);
            if (tokens.Count == 0)
                return transcript;

            var matches = FindMatches(transcript, tokens, aliases);
            if (matches.Count == 0)
            {
                if (_verbose)
                    _onDebug?.Invoke("[STT] no SimBrief name correction applied.");
                return transcript;
            }

            var updated = ApplyMatches(transcript, tokens, matches);
            return updated;
        }
        catch
        {
            return transcript;
        }
    }

    private List<AliasEntry> BuildAliases(FlightContext flight)
    {
        var aliases = new List<AliasEntry>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        AddAirportAliases(aliases, seen, flight.OriginIcao, flight.OriginName);
        AddAirportAliases(aliases, seen, flight.DestinationIcao, flight.DestinationName);

        if (!string.IsNullOrWhiteSpace(flight.AlternateIcao))
        {
            AddAlias(aliases, seen, flight.AlternateIcao.Trim().ToUpperInvariant(),
                flight.AlternateIcao, AliasType.Icao);
        }

        return aliases;
    }

    private static void AddAirportAliases(List<AliasEntry> aliases, HashSet<string> seen, string? icao, string? name)
    {
        if (!string.IsNullOrWhiteSpace(icao))
        {
            var canonicalIcao = icao.Trim().ToUpperInvariant();
            AddAlias(aliases, seen, canonicalIcao, canonicalIcao, AliasType.Icao);
            AddAlias(aliases, seen, canonicalIcao, string.Join(" ", canonicalIcao.ToCharArray()), AliasType.Icao);

            if (canonicalIcao.Length == 4 && canonicalIcao.StartsWith("K", StringComparison.OrdinalIgnoreCase))
            {
                var iata = canonicalIcao[1..].ToUpperInvariant();
                AddAlias(aliases, seen, iata, iata, AliasType.Icao);
                AddAlias(aliases, seen, iata, string.Join(" ", iata.ToCharArray()), AliasType.Icao);
            }
        }

        if (string.IsNullOrWhiteSpace(name))
            return;

        var canonicalName = name.Trim();
        var nameTokens = NormalizeTokens(name).ToList();
        if (nameTokens.Count == 0)
            return;

        AddAlias(aliases, seen, canonicalName, string.Join(" ", nameTokens), AliasType.Name);

        var strippedTokens = nameTokens.Where(t => !StopWords.Contains(t)).ToList();
        if (strippedTokens.Count > 0 && strippedTokens.Count != nameTokens.Count)
        {
            AddAlias(aliases, seen, canonicalName, string.Join(" ", strippedTokens), AliasType.Name);
        }

        var lastToken = strippedTokens.Count > 0 ? strippedTokens[^1] : nameTokens[^1];
        if (lastToken.Length >= 5)
            AddAlias(aliases, seen, canonicalName, lastToken, AliasType.Name);

        var cityAlias = BuildCityAlias(nameTokens);
        if (!string.IsNullOrWhiteSpace(cityAlias))
            AddAlias(aliases, seen, canonicalName, cityAlias, AliasType.City);
    }

    private static string? BuildCityAlias(IReadOnlyList<string> nameTokens)
    {
        if (nameTokens.Count == 2)
            return nameTokens[0];

        if (nameTokens.Count == 3 && StopWords.Contains(nameTokens[2]))
            return string.Join(" ", nameTokens.Take(2));

        return null;
    }

    private List<CorrectionMatch> FindMatches(string transcript, IReadOnlyList<Token> tokens, IReadOnlyList<AliasEntry> aliases)
    {
        var matches = new List<CorrectionMatch>();

        for (int start = 0; start < tokens.Count; start++)
        {
            for (int length = 1; length <= MaxWindowTokens && start + length <= tokens.Count; length++)
            {
                var window = tokens.Skip(start).Take(length).ToList();
                var candidateNormalized = string.Join(" ", window.Select(t => t.Normalized));
                var candidateCollapsed = candidateNormalized.Replace(" ", string.Empty);
                if (candidateCollapsed.Length < 3)
                    continue;

                foreach (var alias in aliases)
                {
                    if (!IsCandidateLengthReasonable(alias, candidateCollapsed.Length))
                        continue;

                    if (alias.Type == AliasType.City && !HasCityContext(tokens, start, start + length - 1))
                        continue;

                    var similarity = ComputeBestSimilarity(candidateNormalized, candidateCollapsed, alias);
                    var threshold = GetThreshold(alias, candidateCollapsed.Length);
                    if (similarity < threshold)
                        continue;

                    var startPos = window[0].Start;
                    var endToken = window[^1];
                    var endPos = endToken.Start + endToken.Length;
                    var original = transcript.Substring(startPos, endPos - startPos);
                    if (string.Equals(original, alias.Canonical, StringComparison.OrdinalIgnoreCase))
                        continue;

                    matches.Add(new CorrectionMatch(start, start + length - 1, startPos, endPos, alias.Canonical, similarity, original));
                }
            }
        }

        return matches;
    }

    private static bool HasCityContext(IReadOnlyList<Token> tokens, int start, int end)
    {
        if (end - start >= 1)
            return true;

        if (start > 0 && IsCityHint(tokens[start - 1].Normalized))
            return true;
        if (end + 1 < tokens.Count && IsCityHint(tokens[end + 1].Normalized))
            return true;

        return false;
    }

    private static bool IsCityHint(string token)
    {
        return token is "airport" or "international" or "arrival" or "departure" or "destination" or "diverting";
    }

    private static bool IsCandidateLengthReasonable(AliasEntry alias, int candidateLength)
    {
        if (alias.Collapsed.Length < 3 || candidateLength < 3)
            return false;
        return true;
    }

    private static double ComputeBestSimilarity(string candidateNormalized, string candidateCollapsed, AliasEntry alias)
    {
        var spaced = Similarity(candidateNormalized, alias.Normalized);
        var collapsed = Similarity(candidateCollapsed, alias.Collapsed);
        return Math.Max(spaced, collapsed);
    }

    private static double GetThreshold(AliasEntry alias, int candidateLength)
    {
        if (alias.Type == AliasType.Icao)
            return Math.Max(IcaoThreshold, candidateLength <= 4 ? ShortThreshold : IcaoThreshold);

        if (alias.Type == AliasType.City)
            return Math.Max(CityThreshold, candidateLength <= 4 ? ShortThreshold : CityThreshold);

        if (alias.Collapsed.Length >= 8 && candidateLength >= 8)
            return LongPhraseThreshold;

        if (alias.Collapsed.Length <= 5 || candidateLength <= 5)
            return Math.Max(DefaultThreshold, ShortThreshold);

        return DefaultThreshold;
    }

    private string ApplyMatches(string transcript, IReadOnlyList<Token> tokens, IReadOnlyList<CorrectionMatch> matches)
    {
        var ordered = matches
            .OrderByDescending(m => m.Score)
            .ThenByDescending(m => m.EndPos - m.StartPos)
            .ToList();

        var used = new bool[tokens.Count];
        var accepted = new List<CorrectionMatch>();
        foreach (var match in ordered)
        {
            if (match.StartTokenIndex < 0 || match.EndTokenIndex >= used.Length)
                continue;

            var overlap = false;
            for (int i = match.StartTokenIndex; i <= match.EndTokenIndex; i++)
            {
                if (used[i])
                {
                    overlap = true;
                    break;
                }
            }
            if (overlap)
                continue;

            for (int i = match.StartTokenIndex; i <= match.EndTokenIndex; i++)
                used[i] = true;

            accepted.Add(match);
        }

        if (accepted.Count == 0)
        {
            if (_verbose)
                _onDebug?.Invoke("[STT] no SimBrief name correction applied.");
            return transcript;
        }

        var sb = new StringBuilder(transcript);
        foreach (var match in accepted.OrderByDescending(m => m.StartPos))
        {
            sb.Remove(match.StartPos, match.EndPos - match.StartPos);
            sb.Insert(match.StartPos, match.Replacement);
            _onDebug?.Invoke($"[STT] corrected {match.Original} -> {match.Replacement} ({match.Score:0.00})");
        }

        return sb.ToString();
    }

    private static List<Token> Tokenize(string text)
    {
        var tokens = new List<Token>();
        foreach (Match match in TokenRegex.Matches(text))
        {
            var raw = match.Value;
            var normalized = NormalizeToken(raw);
            tokens.Add(new Token(match.Index, match.Length, raw, normalized));
        }
        return tokens;
    }

    private static IEnumerable<string> NormalizeTokens(string text)
    {
        foreach (Match match in TokenRegex.Matches(text))
        {
            var normalized = NormalizeToken(match.Value);
            if (!string.IsNullOrWhiteSpace(normalized))
                yield return normalized;
        }
    }

    private static string NormalizeToken(string token)
    {
        var lower = token.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(lower))
            return string.Empty;
        return LetterAliases.TryGetValue(lower, out var letter) ? letter : lower;
    }

    private static double Similarity(string a, string b)
    {
        if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b))
            return 0;

        var distance = LevenshteinDistance(a, b);
        var max = Math.Max(a.Length, b.Length);
        if (max == 0)
            return 1;
        return 1.0 - (double)distance / max;
    }

    private static int LevenshteinDistance(string a, string b)
    {
        var lenA = a.Length;
        var lenB = b.Length;
        var dp = new int[lenA + 1, lenB + 1];

        for (int i = 0; i <= lenA; i++)
            dp[i, 0] = i;
        for (int j = 0; j <= lenB; j++)
            dp[0, j] = j;

        for (int i = 1; i <= lenA; i++)
        {
            for (int j = 1; j <= lenB; j++)
            {
                var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                dp[i, j] = Math.Min(
                    Math.Min(dp[i - 1, j] + 1, dp[i, j - 1] + 1),
                    dp[i - 1, j - 1] + cost);
            }
        }

        return dp[lenA, lenB];
    }

    private sealed record Token(int Start, int Length, string Raw, string Normalized);

    private sealed record AliasEntry(string Canonical, string Normalized, string Collapsed, AliasType Type);

    private sealed record CorrectionMatch(
        int StartTokenIndex,
        int EndTokenIndex,
        int StartPos,
        int EndPos,
        string Replacement,
        double Score,
        string Original);

    private enum AliasType
    {
        Icao,
        Name,
        City
    }

    private static void AddAlias(List<AliasEntry> aliases, HashSet<string> seen, string canonical, string alias, AliasType type)
    {
        var normalized = string.Join(" ", NormalizeTokens(alias));
        if (string.IsNullOrWhiteSpace(normalized))
            return;
        var collapsed = normalized.Replace(" ", string.Empty);
        if (collapsed.Length < 3)
            return;
        var key = $"{type}:{normalized}";
        if (!seen.Add(key))
            return;
        aliases.Add(new AliasEntry(canonical, normalized, collapsed, type));
    }
}
