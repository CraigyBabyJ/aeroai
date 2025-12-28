namespace AeroAI.Atc;

/// <summary>
/// Represents a routing decision for a pilot transmission.
/// </summary>
public sealed class RoutingDecision
{
    public string RawTranscript { get; init; } = string.Empty;
    public string NormalizedTranscript { get; init; } = string.Empty;
    public double? SttConfidence { get; init; }
    public ProceduralIntent? MatchedIntent { get; init; }
    public string RouteTaken { get; init; } = string.Empty; // "Procedural", "LLM", "SayAgain"
    public string Reason { get; init; } = string.Empty;
    public string? ExtractedCallsign { get; init; }
    public bool IsUsable { get; init; }
    public string? UnusableReason { get; init; }
    public long? EstimatedTokens { get; init; }
    public double? EstimatedCost { get; init; }
    
    // Resolved context fields
    public string? SimbriefCallsign { get; init; }
    public string? SpokenCallsign { get; init; }
    public string? DepIcao { get; init; }
    public string? ArrIcao { get; init; }
    public string? DepSpoken { get; init; }
    public string? ArrSpoken { get; init; }
    public string? DepSource { get; init; }
    public string? ArrSource { get; init; }
}

