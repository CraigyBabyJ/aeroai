using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace AeroAI.AtcSession;

public sealed class AtcIntentPack
{
    public List<AtcIntentDefinition> Intents { get; init; } = new();
    [JsonPropertyName("default_threshold")]
    public double? DefaultThreshold { get; init; }
}

public sealed class AtcIntentDefinition
{
    public string Id { get; init; } = string.Empty;
    [JsonPropertyName("required_slots")]
    public List<string> RequiredSlots { get; init; } = new();
    [JsonPropertyName("allowed_phases")]
    public List<string> AllowedPhases { get; init; } = new();
    [JsonPropertyName("score_rules")]
    public List<AtcScoreRule> ScoreRules { get; init; } = new();
    [JsonPropertyName("min_score")]
    public double? MinScore { get; init; }
}

public sealed class AtcScoreRule
{
    public string? Id { get; init; }
    public List<string> Keywords { get; init; } = new();
    public List<string> Regex { get; init; } = new();
    public double Boost { get; init; } = 0.0;
    [JsonPropertyName("expected_next_boost")]
    public double ExpectedNextBoost { get; init; } = 0.0;
}

public sealed class AtcFlowPack
{
    public List<AtcPhaseDefinition> Phases { get; init; } = new();
    [JsonPropertyName("role_phase_map")]
    public Dictionary<string, string> RolePhaseMap { get; init; } = new();
}

public sealed class AtcPhaseDefinition
{
    public string Id { get; init; } = string.Empty;
    [JsonPropertyName("allowed_intents")]
    public List<string> AllowedIntents { get; init; } = new();
    [JsonPropertyName("default_controller_role")]
    public string? DefaultControllerRole { get; init; }
    [JsonPropertyName("expected_next_intents")]
    public List<string> ExpectedNextIntents { get; init; } = new();
    public List<AtcFlowTransition> Transitions { get; init; } = new();
    [JsonPropertyName("fallback_template")]
    public string? FallbackTemplate { get; init; }
}

public sealed class AtcFlowTransition
{
    public string Intent { get; init; } = string.Empty;
    [JsonPropertyName("required_slots")]
    public List<string> RequiredSlots { get; init; } = new();
    [JsonPropertyName("requires_pending_handoff")]
    public bool RequiresPendingHandoff { get; init; }
    [JsonPropertyName("next_state")]
    public string? NextPhase { get; init; }
    [JsonPropertyName("atc_action")]
    public string? AtcAction { get; init; }
    public string? Template { get; init; }
    [JsonPropertyName("set_pending_handoff")]
    public bool SetPendingHandoff { get; init; }
    [JsonPropertyName("commit_pending_handoff")]
    public bool CommitPendingHandoff { get; init; }
    public AtcHandoffSpec? Handoff { get; init; }
}

public sealed class AtcHandoffSpec
{
    public string Role { get; init; } = string.Empty;
    [JsonPropertyName("frequency_mhz")]
    public double? FrequencyMhz { get; init; }
}

public sealed class AtcTemplatePack
{
    public List<AtcTemplateDefinition> Templates { get; init; } = new();
}

public sealed class AtcTemplateDefinition
{
    public string Id { get; init; } = string.Empty;
    public string? Phase { get; init; }
    public string? Intent { get; init; }
    [JsonPropertyName("atc_action")]
    public string? AtcAction { get; init; }
    public string? Text { get; init; }
    public List<string> Variants { get; init; } = new();
    [JsonPropertyName("requires_readback")]
    public bool RequiresReadback { get; init; }
    [JsonPropertyName("readback_items")]
    public List<string> ReadbackItems { get; init; } = new();
}
