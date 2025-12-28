using System;
using System.Linq;
using System.Text.RegularExpressions;

namespace AeroAI.Atc;

/// <summary>
/// Determines if a transcript is usable for LLM processing.
/// </summary>
public static class TranscriptUsabilityChecker
{
    private static readonly string[] FillerWords = new[]
    {
        "uh", "um", "erm", "ah", "eh", "hmm",
        "hello", "test", "testing"
        // Note: "mic" and "check" removed - "radio check" is a valid phrase, not filler
    };

    private static readonly Regex CallsignPattern = new(
        @"\b([A-Z]{2,4}\s*\d{1,4}[A-Z]?|[A-Z]\d{1,4})\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// Checks if a transcript is usable for LLM processing.
    /// </summary>
    /// <param name="transcript">The raw transcript text</param>
    /// <param name="sttConfidence">Optional STT confidence score (0.0-1.0)</param>
    /// <param name="minConfidence">Minimum confidence threshold (default 0.0 = no threshold)</param>
    /// <returns>True if transcript is usable, false otherwise</returns>
    public static bool IsUsable(string? transcript, double? sttConfidence = null, double minConfidence = 0.0)
    {
        // Check confidence threshold if provided
        if (sttConfidence.HasValue && sttConfidence.Value < minConfidence)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(transcript))
        {
            return false;
        }

        var trimmed = transcript.Trim();
        
        // Too short
        if (trimmed.Length < 3)
        {
            return false;
        }

        var tokens = trimmed.Split(new[] { ' ', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
        
        // Too few tokens
        if (tokens.Length < 2)
        {
            // Exception: single token that looks like a callsign (e.g., "Easy123", "BAW456")
            if (tokens.Length == 1 && CallsignPattern.IsMatch(tokens[0]))
            {
                return true;
            }
            return false;
        }

        // All tokens are filler words
        if (tokens.All(t => FillerWords.Contains(t.ToLowerInvariant())))
        {
            return false;
        }

        // Has at least one non-filler token
        return true;
    }

    /// <summary>
    /// Gets a reason why a transcript is unusable, or null if usable.
    /// </summary>
    public static string? GetUnusableReason(string? transcript, double? sttConfidence = null, double minConfidence = 0.0)
    {
        if (sttConfidence.HasValue && sttConfidence.Value < minConfidence)
        {
            return $"STT confidence below threshold: {sttConfidence.Value:F2} < {minConfidence:F2}";
        }

        if (string.IsNullOrWhiteSpace(transcript))
        {
            return "Empty or whitespace only";
        }

        var trimmed = transcript.Trim();
        
        if (trimmed.Length < 3)
        {
            return $"Too short: {trimmed.Length} characters";
        }

        var tokens = trimmed.Split(new[] { ' ', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
        
        if (tokens.Length < 2)
        {
            if (tokens.Length == 1 && CallsignPattern.IsMatch(tokens[0]))
            {
                return null; // Usable (callsign)
            }
            return $"Too few tokens: {tokens.Length}";
        }

        if (tokens.All(t => FillerWords.Contains(t.ToLowerInvariant())))
        {
            return "All tokens are filler words";
        }

        return null; // Usable
    }
}

