using System;
using System.Collections.Generic;
using System.Linq;

namespace AeroAI.AtcSession;

public sealed class AtcFlowEngine
{
    private readonly AtcPackStore _packs;

    public AtcFlowEngine(AtcPackStore packs)
    {
        _packs = packs ?? throw new ArgumentNullException(nameof(packs));
    }

    public AtcFlowDecision Resolve(AtcSessionState state, AtcIntentResult intent)
    {
        var phase = ResolvePhase(state.CurrentPhase);
        if (phase == null)
        {
            return new AtcFlowDecision(state.CurrentPhase, null, null, null, false, false);
        }

        if (phase.AllowedIntents.Count > 0 &&
            !phase.AllowedIntents.Any(i => i.Equals(intent.IntentId, StringComparison.OrdinalIgnoreCase)))
        {
            return new AtcFlowDecision(phase.Id, phase.FallbackTemplate, "SAY_AGAIN", null, false, false);
        }

        foreach (var transition in phase.Transitions)
        {
            if (!transition.Intent.Equals(intent.IntentId, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if ((transition.RequiresPendingHandoff || transition.CommitPendingHandoff) &&
                state.PendingHandoff == null)
            {
                continue;
            }

            if (transition.RequiredSlots.Count > 0 &&
                !HasRequiredSlots(transition.RequiredSlots, intent.ExtractedSlots))
            {
                continue;
            }

            var nextPhase = transition.NextPhase ?? phase.Id;
            var atcAction = transition.AtcAction ?? transition.Template ?? intent.IntentId;

            return new AtcFlowDecision(
                nextPhase,
                transition.Template ?? phase.FallbackTemplate,
                atcAction,
                transition.Handoff,
                transition.SetPendingHandoff,
                transition.CommitPendingHandoff);
        }

        if (!string.IsNullOrWhiteSpace(phase.FallbackTemplate))
        {
            return new AtcFlowDecision(phase.Id, phase.FallbackTemplate, "SAY_AGAIN", null, false, false);
        }

        return new AtcFlowDecision(phase.Id, null, null, null, false, false);
    }

    public IReadOnlyList<string> GetAllowedActions(string? phaseId)
    {
        var phase = ResolvePhase(phaseId);
        if (phase == null)
        {
            return Array.Empty<string>();
        }

        var actions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var transition in phase.Transitions)
        {
            if (!string.IsNullOrWhiteSpace(transition.AtcAction))
            {
                actions.Add(transition.AtcAction);
            }
        }

        return actions.ToList();
    }

    public IReadOnlyList<string> GetExpectedNextIntents(string? phaseId)
    {
        var phase = ResolvePhase(phaseId);
        return phase?.ExpectedNextIntents ?? new List<string>();
    }

    public string GetDefaultControllerRole(string? phaseId)
    {
        var phase = ResolvePhase(phaseId);
        return phase?.DefaultControllerRole ?? "delivery";
    }

    public string GetPhaseForRole(string? role, string? fallbackPhase = null)
    {
        if (!string.IsNullOrWhiteSpace(role) &&
            _packs.RolePhaseMap.TryGetValue(role, out var mapped) &&
            !string.IsNullOrWhiteSpace(mapped))
        {
            return mapped;
        }

        return role?.Trim().ToLowerInvariant() switch
        {
            "delivery" => "clearance",
            "clearance" => "clearance",
            "ground" => "ground",
            "tower" => "tower",
            "departure" => "departure",
            "center" => "center",
            "approach" => "approach",
            _ => fallbackPhase ?? _packs.Flows.Phases.FirstOrDefault()?.Id ?? "clearance"
        };
    }

    private AtcPhaseDefinition? ResolvePhase(string? phaseId)
    {
        if (!string.IsNullOrWhiteSpace(phaseId) && _packs.PhaseById.TryGetValue(phaseId, out var found))
        {
            return found;
        }

        return _packs.Flows.Phases.FirstOrDefault();
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
}

public sealed record AtcFlowDecision(
    string NextPhaseId,
    string? TemplateId,
    string? AtcAction,
    AtcHandoffSpec? Handoff,
    bool SetPendingHandoff,
    bool CommitPendingHandoff);
