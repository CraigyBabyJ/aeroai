using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using AeroAI.Atc;

namespace AeroAI.AtcSession;

public sealed class AtcPromptBuilder
{
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true
    };

    public string BuildUserPrompt(AtcContext context, AtcPromptData promptData, string pilotTransmission)
    {
        var payload = new
        {
            session = new
            {
                id = promptData.SessionId,
                phase = promptData.Phase,
                controller_role = promptData.ControllerRole,
                active_controller_role = promptData.ActiveControllerRole,
                active_frequency_mhz = promptData.ActiveFrequencyMhz,
                last_action = promptData.LastAtcAction,
                pending_handoff = promptData.PendingHandoff,
                expected_next_intents = promptData.ExpectedNextIntents
            },
            extracted_slots = promptData.ExtractedSlots,
            simbrief = promptData.SimBrief,
            allowed_actions = promptData.AllowedActions,
            template = promptData.Template,
            atc_context = context
        };

        var json = JsonSerializer.Serialize(payload, _jsonOptions);

        var sb = new StringBuilder();
        sb.AppendLine("ATC_SESSION_JSON:");
        sb.AppendLine("```json");
        sb.AppendLine(json);
        sb.AppendLine("```");
        sb.AppendLine();
        sb.AppendLine("PILOT_TRANSMISSION:");
        sb.Append('"');
        sb.Append(pilotTransmission);
        sb.AppendLine("\"");
        sb.AppendLine();
        sb.AppendLine("Using ONLY this information and the template intent, respond with a single ICAO-style ATC transmission.");
        return sb.ToString();
    }
}

public sealed class AtcPromptData
{
    public string SessionId { get; init; } = string.Empty;
    public string Phase { get; init; } = string.Empty;
    public string ControllerRole { get; init; } = string.Empty;
    public string? ActiveControllerRole { get; init; }
    public string? ActiveFrequencyMhz { get; init; }
    public string? LastAtcAction { get; init; }
    public object? PendingHandoff { get; init; }
    public IReadOnlyList<string> ExpectedNextIntents { get; init; } = new List<string>();
    public IReadOnlyDictionary<string, string> ExtractedSlots { get; init; } = new Dictionary<string, string>();
    public object? SimBrief { get; init; }
    public IReadOnlyList<string> AllowedActions { get; init; } = new List<string>();
    public object? Template { get; init; }
}
