namespace AeroAI.Atc;

/// <summary>
/// Procedural intents that are handled by hard-coded logic before LLM routing.
/// These are procedural ATC interactions that must never require LLM interpretation.
/// </summary>
public enum ProceduralIntent
{
    /// <summary>
    /// No procedural intent matched.
    /// </summary>
    None,

    /// <summary>
    /// Radio check / mic check - procedural response required.
    /// </summary>
    RadioCheck
}

