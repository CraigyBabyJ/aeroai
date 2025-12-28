using System;
using System.Linq;
using System.Text.RegularExpressions;
using AeroAI.Data;

namespace AeroAI.Atc;

/// <summary>
/// Routes pilot transmissions to procedural intent handlers that bypass the LLM.
/// These handlers use hard-coded logic for procedural ATC interactions like radio checks.
/// </summary>
public static class ProceduralIntentRouter
{
    // Radio check patterns - forgiving matching
    // Matches: "radio check", "radio checker", "radio checking", "mic check", etc.
    private static readonly Regex RadioCheckPattern = new(
        @"\b(?:radio\s+check(?:er|ing)?|mic\s+check(?:er|ing)?)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Filler words to ignore
    private static readonly string[] FillerWords = { "uh", "um", "please", "request", "uhm", "er" };

    // Radio check response templates - Standard ICAO/Global (most common)
    // These are safe anywhere in the world
    private static readonly string[] RadioCheckResponses = new[]
    {
        "{CALLSIGN}, loud and clear.",
        "{CALLSIGN}, readability five.",
        "{CALLSIGN}, five by five.",
        "{CALLSIGN}, loud and clear, readability five."
    };

    // North American variations (common in Canada/US)
    private static readonly string[] RadioCheckResponsesNorthAmerica = new[]
    {
        "{CALLSIGN}, loud and clear.",
        "{CALLSIGN}, loud and clear, go ahead.",
        "{CALLSIGN}, five by five, go ahead."
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
    /// <param name="resolvedContext">Optional resolved context for authoritative callsign/airport data.</param>
    /// <returns>ProceduralIntentResult indicating if a match was found and the response.</returns>
    public static ProceduralIntentResult TryMatch(string transcript, FlightContext context, Action<string>? onDebug = null, ResolvedContext? resolvedContext = null)
    {
        if (string.IsNullOrWhiteSpace(transcript))
            return ProceduralIntentResult.NoMatch();

        // Try to match radio check
        var radioCheckResult = TryMatchRadioCheck(transcript, context, onDebug, resolvedContext);
        if (radioCheckResult.Matched)
            return radioCheckResult;

        return ProceduralIntentResult.NoMatch();
    }

    private static ProceduralIntentResult TryMatchRadioCheck(string transcript, FlightContext context, Action<string>? onDebug, ResolvedContext? resolvedContext = null)
    {
        // Check if transcript contains radio check pattern
        if (!RadioCheckPattern.IsMatch(transcript))
            return ProceduralIntentResult.NoMatch();

        // Normalize transcript: remove filler words for better matching
        var normalized = RemoveFillerWords(transcript);

        // Extract callsign - ALWAYS prefer resolved context (authoritative SimBrief data)
        // This is critical because STT may mishear airline names (e.g., "commissar" instead of "Air Canada")
        string? callsign = null;
        
        // Prefer spoken callsign from resolved context (authoritative SimBrief data)
        // This ensures we use the correct callsign even if STT misheard it
        if (resolvedContext != null && !string.IsNullOrWhiteSpace(resolvedContext.CallsignSpoken))
        {
            callsign = resolvedContext.CallsignSpoken;
        }
        else
        {
            // If resolved context not available, use RadioCallsign from context (spoken form)
            // This is more reliable than trying to extract from misheard transcript
            if (!string.IsNullOrWhiteSpace(context.RadioCallsign))
            {
                callsign = context.RadioCallsign;
            }
            else
            {
                // Last resort: try to extract from transcript (may be misheard)
                callsign = ExtractCallsignFromRadioCheck(normalized, context);
            }
        }

        // Determine if we're in North America (Canada/US) for regional phraseology
        var isNorthAmerica = IsNorthAmericanAirport(context);

        // Generate response with regional variations
        var response = GenerateRadioCheckResponse(callsign, isNorthAmerica);

        // Log the match
        var logMessage = $"[IntentRouter] Matched procedural intent: RadioCheck, callsign={callsign ?? "null"}, region={(isNorthAmerica ? "NorthAmerica" : "Global")}, transcript=\"{transcript}\"";
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

    private static string GenerateRadioCheckResponse(string? callsign, bool isNorthAmerica = false)
    {
        string[] templates;
        if (!string.IsNullOrWhiteSpace(callsign))
        {
            // Use North American templates if in North America, otherwise use global/ICAO templates
            templates = isNorthAmerica ? RadioCheckResponsesNorthAmerica : RadioCheckResponses;
        }
        else
        {
            templates = RadioCheckResponsesNoCallsign;
        }

        // Randomly select a template from the appropriate pool
        var template = templates[_random.Next(templates.Length)];
        return template.Replace("{CALLSIGN}", callsign ?? string.Empty).Trim();
    }

    /// <summary>
    /// Determines if the airport is in North America (Canada or United States).
    /// Checks ICAO prefix (C for Canada, K for US) or ISO country code.
    /// </summary>
    private static bool IsNorthAmericanAirport(FlightContext context)
    {
        if (context == null)
            return false;

        // Check origin airport first (most relevant for clearance delivery)
        var icao = context.OriginIcao;
        if (string.IsNullOrWhiteSpace(icao))
            return false;

        var upperIcao = icao.Trim().ToUpperInvariant();

        // Check ICAO prefix: C = Canada, K = United States
        if (upperIcao.StartsWith("C", StringComparison.Ordinal) || 
            upperIcao.StartsWith("K", StringComparison.Ordinal))
        {
            return true;
        }

        // Fallback: Check ISO country code via AirportDataService
        if (AirportDataService.TryGetAirportInfo(icao, out var info))
        {
            var isoCountry = info.IsoCountry?.Trim().ToUpperInvariant();
            if (isoCountry == "CA" || isoCountry == "US")
            {
                return true;
            }
        }

        return false;
    }
}

