using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace AeroAI.AtcSession;

public sealed class IntentEngine
{
    private readonly AtcPackStore _packs;
    private readonly IIntentClassifier? _fallbackClassifier;
    private readonly double _defaultThreshold;

    public IntentEngine(AtcPackStore packs, IIntentClassifier? fallbackClassifier = null, double defaultThreshold = 0.6)
    {
        _packs = packs ?? throw new ArgumentNullException(nameof(packs));
        _fallbackClassifier = fallbackClassifier;
        _defaultThreshold = defaultThreshold;
    }

    public async Task<AtcIntentResult> ClassifyAsync(string transcript, AtcIntentContext context, CancellationToken ct = default)
    {
        var normalized = Normalize(transcript);
        var extractedSlots = ExtractSlots(transcript);

        var best = new AtcIntentResult("unknown", 0.0, extractedSlots, new List<AtcMatchedRule>(), "rules");
        foreach (var intent in _packs.Intents.Intents)
        {
            if (string.IsNullOrWhiteSpace(intent.Id))
            {
                continue;
            }

            if (intent.AllowedPhases.Count > 0 &&
                !intent.AllowedPhases.Any(p => p.Equals(context.CurrentPhase, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            var matchedRules = new List<AtcMatchedRule>();
            var score = 0.0;
            for (var i = 0; i < intent.ScoreRules.Count; i++)
            {
                var rule = intent.ScoreRules[i];
                if (IsRuleMatch(rule, normalized, out var matchedKeywords, out var matchedRegex))
                {
                    var ruleScore = rule.Boost;
                    if (context.ExpectedNextIntents.Any(id => id.Equals(intent.Id, StringComparison.OrdinalIgnoreCase)))
                    {
                        ruleScore += rule.ExpectedNextBoost;
                    }

                    score += ruleScore;
                    matchedRules.Add(new AtcMatchedRule(rule.Id ?? $"{intent.Id}:{i}", ruleScore, matchedKeywords, matchedRegex));
                }
            }

            if (intent.RequiredSlots.Count > 0 && !HasRequiredSlots(intent.RequiredSlots, extractedSlots))
            {
                score *= 0.6;
            }

            var threshold = intent.MinScore ?? _packs.Intents.DefaultThreshold ?? _defaultThreshold;
            var confidence = Math.Min(1.0, score);

            if (confidence >= threshold && confidence > best.Confidence)
            {
                best = new AtcIntentResult(intent.Id, confidence, extractedSlots, matchedRules, "rules");
            }
        }

        var fallbackThreshold = _packs.Intents.DefaultThreshold ?? _defaultThreshold;
        if (best.Confidence < fallbackThreshold && _fallbackClassifier != null)
        {
            var fallback = await _fallbackClassifier.ClassifyAsync(transcript, context, ct);
            if (fallback != null)
            {
                return fallback with { ExtractedSlots = extractedSlots };
            }
        }

        return best;
    }

    private static bool IsRuleMatch(AtcScoreRule rule, string normalized, out List<string> matchedKeywords, out List<string> matchedRegex)
    {
        matchedKeywords = new List<string>();
        matchedRegex = new List<string>();
        bool matched = false;

        foreach (var keyword in rule.Keywords)
        {
            var normalizedKeyword = Normalize(keyword);
            if (string.IsNullOrWhiteSpace(normalizedKeyword))
            {
                continue;
            }

            if (normalized.Contains(normalizedKeyword, StringComparison.Ordinal))
            {
                matched = true;
                matchedKeywords.Add(keyword);
            }
        }

        foreach (var pattern in rule.Regex)
        {
            if (string.IsNullOrWhiteSpace(pattern))
            {
                continue;
            }

            if (Regex.IsMatch(normalized, pattern, RegexOptions.IgnoreCase))
            {
                matched = true;
                matchedRegex.Add(pattern);
            }
        }

        return matched;
    }

    private static bool HasRequiredSlots(IEnumerable<string> requiredSlots, IReadOnlyDictionary<string, string> extractedSlots)
    {
        foreach (var slot in requiredSlots)
        {
            if (!extractedSlots.TryGetValue(slot, out var value) || string.IsNullOrWhiteSpace(value))
            {
                return false;
            }
        }

        return true;
    }

    private static string Normalize(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var lower = text.ToLowerInvariant();
        lower = Regex.Replace(lower, @"[^a-z0-9\s\.]", " ");
        lower = Regex.Replace(lower, @"\s+", " ").Trim();
        return lower;
    }

    private static Dictionary<string, string> ExtractSlots(string transcript)
    {
        var slots = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(transcript))
        {
            return slots;
        }

        var runwayMatch = Regex.Match(transcript, @"\brunway\s+(?<rwy>\d{1,2}[LRC]?)\b", RegexOptions.IgnoreCase);
        if (runwayMatch.Success)
        {
            slots["runway"] = runwayMatch.Groups["rwy"].Value.ToUpperInvariant();
        }

        var freqMatch = Regex.Match(transcript, @"\b(?<freq>\d{3}\.\d{3})\b");
        if (freqMatch.Success)
        {
            slots["frequency"] = freqMatch.Groups["freq"].Value;
        }

        var altitudeMatch = Regex.Match(transcript, @"\b(?:flight level|FL)\s*(?<fl>\d{2,3})\b", RegexOptions.IgnoreCase);
        if (altitudeMatch.Success)
        {
            slots["altitude"] = $"FL{altitudeMatch.Groups["fl"].Value}";
        }

        return slots;
    }
}

public sealed record AtcIntentResult(
    string IntentId,
    double Confidence,
    IReadOnlyDictionary<string, string> ExtractedSlots,
    IReadOnlyList<AtcMatchedRule> MatchedRules,
    string Source);

public sealed record AtcMatchedRule(
    string RuleId,
    double Score,
    IReadOnlyList<string> MatchedKeywords,
    IReadOnlyList<string> MatchedRegex);

public sealed record AtcIntentContext(
    string CurrentPhase,
    IReadOnlyList<string> ExpectedNextIntents,
    IReadOnlyList<string> AllowedIntents);

public interface IIntentClassifier
{
    Task<AtcIntentResult?> ClassifyAsync(string transcript, AtcIntentContext context, CancellationToken ct = default);
}
