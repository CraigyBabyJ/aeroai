namespace AeroAI.Atc;

/// <summary>
/// Result of procedural intent matching. If Matched is true, the response should bypass LLM.
/// </summary>
public sealed class ProceduralIntentResult
{
    /// <summary>
    /// Whether a procedural intent was matched.
    /// </summary>
    public bool Matched { get; init; }

    /// <summary>
    /// The matched intent type.
    /// </summary>
    public ProceduralIntent Intent { get; init; }

    /// <summary>
    /// The hard-coded ATC response text (only set if Matched is true).
    /// </summary>
    public string? ResponseText { get; init; }

    /// <summary>
    /// The extracted callsign (if any) from the transmission.
    /// </summary>
    public string? ExtractedCallsign { get; init; }

    /// <summary>
    /// The original transcript that was matched.
    /// </summary>
    public string? OriginalTranscript { get; init; }

    public static ProceduralIntentResult NoMatch() => new()
    {
        Matched = false,
        Intent = ProceduralIntent.None
    };

    public static ProceduralIntentResult Match(ProceduralIntent intent, string responseText, string? extractedCallsign, string originalTranscript) => new()
    {
        Matched = true,
        Intent = intent,
        ResponseText = responseText,
        ExtractedCallsign = extractedCallsign,
        OriginalTranscript = originalTranscript
    };
}

