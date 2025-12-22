using System.Collections.Generic;
using System.Linq;

namespace AeroAI.AtcSession;

public sealed class AtcPackStore
{
    public AtcPackStore(AtcIntentPack intents, AtcFlowPack flows, AtcTemplatePack templates)
    {
        Intents = intents;
        Flows = flows;
        Templates = templates;
        IntentById = intents.Intents
            .Where(i => !string.IsNullOrWhiteSpace(i.Id))
            .ToDictionary(i => i.Id, i => i, System.StringComparer.OrdinalIgnoreCase);
        PhaseById = flows.Phases
            .Where(s => !string.IsNullOrWhiteSpace(s.Id))
            .ToDictionary(s => s.Id, s => s, System.StringComparer.OrdinalIgnoreCase);
        TemplateById = templates.Templates
            .Where(t => !string.IsNullOrWhiteSpace(t.Id))
            .ToDictionary(t => t.Id, t => t, System.StringComparer.OrdinalIgnoreCase);
        RolePhaseMap = flows.RolePhaseMap
            .Where(kvp => !string.IsNullOrWhiteSpace(kvp.Key) && !string.IsNullOrWhiteSpace(kvp.Value))
            .ToDictionary(kvp => kvp.Key, kvp => kvp.Value, System.StringComparer.OrdinalIgnoreCase);
    }

    public AtcIntentPack Intents { get; }
    public AtcFlowPack Flows { get; }
    public AtcTemplatePack Templates { get; }
    public IReadOnlyDictionary<string, AtcIntentDefinition> IntentById { get; }
    public IReadOnlyDictionary<string, AtcPhaseDefinition> PhaseById { get; }
    public IReadOnlyDictionary<string, AtcTemplateDefinition> TemplateById { get; }
    public IReadOnlyDictionary<string, string> RolePhaseMap { get; }
}
