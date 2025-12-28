using System;
using System.Text.Json;

namespace AeroAI.Atc;

/// <summary>
/// Logs routing decisions for analysis and optimization.
/// </summary>
public static class RoutingDecisionLogger
{
    /// <summary>
    /// Logs a routing decision to the debug pipeline.
    /// </summary>
    public static void LogDecision(RoutingDecision decision, Action<string>? onDebug, RoutingMetrics? metrics = null)
    {
        if (onDebug == null)
            return;

        // Build structured log entry
        var logEntry = new
        {
            category = "IntentRouter",
            timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss.fff"),
            raw_transcript = decision.RawTranscript,
            normalized_transcript = decision.NormalizedTranscript,
            stt_confidence = decision.SttConfidence,
            matched_intent = decision.MatchedIntent?.ToString() ?? "None",
            route_taken = decision.RouteTaken,
            reason = decision.Reason,
            extracted_callsign = decision.ExtractedCallsign,
            is_usable = decision.IsUsable,
            unusable_reason = decision.UnusableReason,
            estimated_tokens = decision.EstimatedTokens,
            estimated_cost = decision.EstimatedCost,
            simbrief_callsign = decision.SimbriefCallsign,
            spoken_callsign = decision.SpokenCallsign,
            dep_icao = decision.DepIcao,
            arr_icao = decision.ArrIcao,
            dep_spoken = decision.DepSpoken,
            arr_spoken = decision.ArrSpoken,
            dep_source = decision.DepSource,
            arr_source = decision.ArrSource
        };

        // Format as JSON-like string for readability
        var logMessage = $"[IntentRouter] " +
            $"route={decision.RouteTaken} " +
            $"intent={decision.MatchedIntent?.ToString() ?? "None"} " +
            $"usable={decision.IsUsable} " +
            $"reason=\"{decision.Reason}\" " +
            $"transcript=\"{decision.NormalizedTranscript}\"";

        if (!string.IsNullOrWhiteSpace(decision.ExtractedCallsign))
        {
            logMessage += $" callsign={decision.ExtractedCallsign}";
        }

        if (!string.IsNullOrWhiteSpace(decision.SpokenCallsign))
        {
            logMessage += $" spoken_callsign=\"{decision.SpokenCallsign}\"";
        }

        if (!string.IsNullOrWhiteSpace(decision.DepSpoken))
        {
            logMessage += $" dep=\"{decision.DepSpoken}\"({decision.DepSource})";
        }

        if (!string.IsNullOrWhiteSpace(decision.ArrSpoken))
        {
            logMessage += $" arr=\"{decision.ArrSpoken}\"({decision.ArrSource})";
        }

        if (decision.SttConfidence.HasValue)
        {
            logMessage += $" confidence={decision.SttConfidence.Value:F2}";
        }

        if (!string.IsNullOrWhiteSpace(decision.UnusableReason))
        {
            logMessage += $" unusable_reason=\"{decision.UnusableReason}\"";
        }

        onDebug(logMessage);

        // Also log metrics summary if available
        if (metrics != null)
        {
            var snapshot = metrics.GetSnapshot();
            var metricsLog = $"[IntentRouter.Metrics] " +
                $"total={snapshot.TotalTranscripts} " +
                $"procedural={snapshot.ProceduralHits} ({snapshot.ProceduralHitRate:P1}) " +
                $"llm={snapshot.LlmCalls} ({snapshot.LlmCallRate:P1}) " +
                $"say_again={snapshot.SayAgainCount} ({snapshot.SayAgainRate:P1}) " +
                $"failures={snapshot.LlmFailures}";
            
            // Only log metrics every 10 transcripts to avoid spam
            if (snapshot.TotalTranscripts % 10 == 0)
            {
                onDebug(metricsLog);
            }
        }
    }

    /// <summary>
    /// Logs an LLM failure with details.
    /// </summary>
    public static void LogLlmFailure(string transcript, string reason, Exception? exception, Action<string>? onDebug)
    {
        if (onDebug == null)
            return;

        var logMessage = $"[IntentRouter.LLMFailure] " +
            $"reason=\"{reason}\" " +
            $"transcript=\"{transcript}\"";

        if (exception != null)
        {
            logMessage += $" error=\"{exception.GetType().Name}: {exception.Message}\"";
        }

        onDebug(logMessage);
    }
}

