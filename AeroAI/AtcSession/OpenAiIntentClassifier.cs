using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AeroAI.Llm;

namespace AeroAI.AtcSession;

public sealed class OpenAiIntentClassifier : IIntentClassifier
{
    private readonly ILlmClient _llmClient;
    private readonly AtcPackStore _packs;

    public OpenAiIntentClassifier(ILlmClient llmClient, AtcPackStore packs)
    {
        _llmClient = llmClient ?? throw new ArgumentNullException(nameof(llmClient));
        _packs = packs ?? throw new ArgumentNullException(nameof(packs));
    }

    public async Task<AtcIntentResult?> ClassifyAsync(string transcript, AtcIntentContext context, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(transcript))
        {
            return null;
        }

        var allowed = context.AllowedIntents.Count > 0
            ? _packs.Intents.Intents.Where(i => context.AllowedIntents.Contains(i.Id, StringComparer.OrdinalIgnoreCase))
            : _packs.Intents.Intents;

        var intentList = allowed.Select(i => new
        {
            id = i.Id,
            required_slots = i.RequiredSlots,
            score_rules = i.ScoreRules.Select(r => new { r.Id, r.Keywords, r.Regex })
        });

        var payload = new
        {
            intents = intentList
        };

        var prompt = $"INTENT_JSON:\n```json\n{JsonSerializer.Serialize(payload)}\n```\n" +
                     $"CURRENT_PHASE: {context.CurrentPhase}\n" +
                     $"TRANSCRIPT:\n\"{transcript}\"\n\n" +
                     "Return JSON only: {\"intent\":\"<id>\",\"confidence\":0.0-1.0}.";

        var raw = await _llmClient.GenerateAsync(prompt, ct);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        try
        {
            var jsonStart = raw.IndexOf('{');
            var jsonEnd = raw.LastIndexOf('}');
            if (jsonStart < 0 || jsonEnd <= jsonStart)
            {
                return null;
            }

            var json = raw.Substring(jsonStart, jsonEnd - jsonStart + 1);
            var doc = JsonSerializer.Deserialize<JsonElement>(json);
            var intent = doc.TryGetProperty("intent", out var intentProp) ? intentProp.GetString() : null;
            var confidence = doc.TryGetProperty("confidence", out var confProp) && confProp.TryGetDouble(out var c)
                ? c
                : 0.0;

            if (string.IsNullOrWhiteSpace(intent))
            {
                return null;
            }

            return new AtcIntentResult(intent, confidence, new Dictionary<string, string>(), new List<AtcMatchedRule>(), "openai");
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
