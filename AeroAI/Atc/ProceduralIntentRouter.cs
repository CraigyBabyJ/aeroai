using System;
using System.Linq;
using System.Text.RegularExpressions;

namespace AeroAI.Atc;

/// <summary>
/// Routes pilot transmissions to procedural intent handlers that bypass the LLM.
/// These handlers use hard-coded logic for procedural ATC interactions like radio checks.
/// </summary>
public static class ProceduralIntentRouter
{
    // Radio check patterns - forgiving matching
    private static readonly Regex RadioCheckPattern = new(
        @"\b(?:radio\s+check|mic\s+check|radio\s+checking)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Filler words to ignore
    private static readonly string[] FillerWords = { "uh", "um", "please", "request", "uhm", "er" };

    // Radio check response templates
    private static readonly string[] RadioCheckResponses = new[]
    {
        "{CALLSIGN}, loud and clear.",
        "{CALLSIGN}, readability five.",
        "{CALLSIGN}, loud and clear, good day.",
        "{CALLSIGN}, five by five."
    };

    private static readonly string[] RadioCheckResponsesNoCallsign = new[]
    {
        "Loud and clear.",
        "Readability five.",
        "Loud and clear, good day.",
        "Five by five."
    };

    private static readonly Random _random = new();

    /// <summary>
    /// Attempts to match a procedural intent in the pilot transmission.
    /// This runs BEFORE any LLM routing to handle procedural interactions.
    /// </summary>
    /// <param name="transcript">The normalized pilot transmission transcript.</param>
    /// <param name="context">Flight context for callsign extraction/fallback.</param>
    /// <param name="onDebug">Optional debug logging callback.</param>
    /// <returns>ProceduralIntentResult indicating if a match was found and the response.</returns>
    public static ProceduralIntentResult TryMatch(string transcript, FlightContext context, Action<string>? onDebug = null)
    {
        if (string.IsNullOrWhiteSpace(transcript))
            return ProceduralIntentResult.NoMatch();

        // Try to match radio check
        var radioCheckResult = TryMatchRadioCheck(transcript, context, onDebug);
        if (radioCheckResult.Matched)
            return radioCheckResult;

        return ProceduralIntentResult.NoMatch();
    }

    private static ProceduralIntentResult TryMatchRadioCheck(string transcript, FlightContext context, Action<string>? onDebug)
    {
        // Check if transcript contains radio check pattern
        if (!RadioCheckPattern.IsMatch(transcript))
            return ProceduralIntentResult.NoMatch();

        // Normalize transcript: remove filler words for better matching
        var normalized = RemoveFillerWords(transcript);

        // Extract callsign - try from transcript first, then fallback to context
        var callsign = ExtractCallsignFromRadioCheck(normalized, context);

        // Generate response
        var response = GenerateRadioCheckResponse(callsign);

        // Log the match
        var logMessage = $"[IntentRouter] Matched procedural intent: RadioCheck, callsign={callsign ?? "null"}, transcript=\"{transcript}\"";
        onDebug?.Invoke(logMessage);

        return ProceduralIntentResult.Match(
            ProceduralIntent.RadioCheck,
            response,
            callsign,
            transcript);
    }

    private static string RemoveFillerWords(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return text;

        var words = text.Split(new[] { ' ', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
        var filtered = words.Where(w => !FillerWords.Contains(w, StringComparer.OrdinalIgnoreCase));
        return string.Join(" ", filtered);
    }

    private static string? ExtractCallsignFromRadioCheck(string transcript, FlightContext context)
    {
        // First, normalize spoken numbers to digits for callsign extraction
        // "easy one two three radio check" -> "easy 123 radio check"
        var normalized = SpokenNumberNormalizer.Normalize(transcript);

        // Try to extract callsign from the transcript using existing utilities
        var extracted = CallsignMatcher.ExtractCallsign(normalized, context);
        if (!string.IsNullOrWhiteSpace(extracted))
            return extracted;

        // Fallback: try to extract callsign pattern manually
        // Pattern: word(s) followed by digits, before or after "radio check"
        var beforePattern = new Regex(@"\b([A-Za-z]+(?:\s+[A-Za-z]+)?)\s+(\d+)\s+(?:radio|mic)\s+check", RegexOptions.IgnoreCase);
        var afterPattern = new Regex(@"(?:radio|mic)\s+check\s+([A-Za-z]+(?:\s+[A-Za-z]+)?)\s+(\d+)", RegexOptions.IgnoreCase);

        var beforeMatch = beforePattern.Match(normalized);
        if (beforeMatch.Success)
        {
            var prefix = beforeMatch.Groups[1].Value;
            var number = beforeMatch.Groups[2].Value;
            return $"{prefix} {number}";
        }

        var afterMatch = afterPattern.Match(normalized);
        if (afterMatch.Success)
        {
            var prefix = afterMatch.Groups[1].Value;
            var number = afterMatch.Groups[2].Value;
            return $"{prefix} {number}";
        }

        // Final fallback: use callsign from context if available
        if (!string.IsNullOrWhiteSpace(context.Callsign))
            return context.Callsign;

        if (!string.IsNullOrWhiteSpace(context.CanonicalCallsign))
            return context.CanonicalCallsign;

        return null;
    }

    private static string GenerateRadioCheckResponse(string? callsign)
    {
        string[] templates;
        if (!string.IsNullOrWhiteSpace(callsign))
        {
            templates = RadioCheckResponses;
        }
        else
        {
            templates = RadioCheckResponsesNoCallsign;
        }

        var template = templates[_random.Next(templates.Length)];
        return template.Replace("{CALLSIGN}", callsign ?? string.Empty).Trim();
    }
}

