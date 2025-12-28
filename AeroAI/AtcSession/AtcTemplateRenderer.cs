using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace AeroAI.AtcSession;

public sealed class AtcTemplateRenderer
{
    private static readonly Regex TokenRegex = new(@"\{(?<token>[a-zA-Z0-9_]+)\}", RegexOptions.Compiled);
    private readonly AtcPackStore _packs;
    private readonly Random _random = new();

    public AtcTemplateRenderer(AtcPackStore packs)
    {
        _packs = packs ?? throw new ArgumentNullException(nameof(packs));
    }

    public AtcTemplateRenderResult? TryRender(AtcTemplateRequest request, IReadOnlyDictionary<string, string> data)
    {
        if (request == null)
        {
            return null;
        }

        var template = ResolveTemplate(request);
        if (template == null)
        {
            return null;
        }

        var text = SelectTemplateText(template);
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        text = FillTokens(text, data);
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        return new AtcTemplateRenderResult(text, template.RequiresReadback, template.ReadbackItems);
    }

    public string? RenderMissingInfoPrompt(string slot, string? phase, string? role, IReadOnlyDictionary<string, string> data)
    {
        if (string.IsNullOrWhiteSpace(slot))
        {
            return null;
        }

        var prompt = ResolveMissingInfoPrompt(slot, phase, role);
        if (prompt == null)
        {
            return null;
        }

        var text = SelectText(prompt.Text, prompt.Variants);
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        return FillTokens(text, data);
    }

    public string? RenderReadbackAcknowledgementTail(string? phase, string? role, IReadOnlyDictionary<string, string> data)
    {
        var tail = ResolveReadbackTail(phase, role);
        if (tail == null)
        {
            return null;
        }

        var text = SelectText(tail.Text, tail.Variants);
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        return FillTokens(text, data);
    }

    private AtcTemplateDefinition? ResolveTemplate(AtcTemplateRequest request)
    {
        if (!string.IsNullOrWhiteSpace(request.TemplateId) &&
            _packs.TemplateById.TryGetValue(request.TemplateId, out var found))
        {
            return found;
        }

        return _packs.Templates.Templates.FirstOrDefault(t =>
            string.Equals(t.Phase, request.Phase, StringComparison.OrdinalIgnoreCase)
            && string.Equals(t.Intent, request.IntentId, StringComparison.OrdinalIgnoreCase)
            && string.Equals(t.AtcAction, request.AtcAction, StringComparison.OrdinalIgnoreCase));
    }

    private string? SelectTemplateText(AtcTemplateDefinition template)
    {
        if (!string.IsNullOrWhiteSpace(template.Text))
        {
            return template.Text;
        }

        if (template.Variants.Count == 0)
        {
            return null;
        }

        return template.Variants[_random.Next(template.Variants.Count)];
    }

    private static string FillTokens(string text, IReadOnlyDictionary<string, string> data)
    {
        if (data == null || data.Count == 0)
        {
            return text;
        }

        var tokens = TokenRegex.Matches(text).Select(m => m.Groups["token"].Value).Distinct();
        foreach (var token in tokens)
        {
            if (!data.TryGetValue(token, out var value))
            {
                value = string.Empty;
            }

            text = text.Replace("{" + token + "}", value ?? string.Empty, StringComparison.Ordinal);
        }

        text = Regex.Replace(text, @"\s{2,}", " ").Trim();
        return text;
    }

    private AtcMissingInfoPrompt? ResolveMissingInfoPrompt(string slot, string? phase, string? role)
    {
        var prompts = _packs.MissingInfo.Prompts.Where(p =>
                string.Equals(p.Slot, slot, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (prompts.Count == 0)
        {
            return null;
        }

        var ranked = prompts
            .OrderByDescending(p => MatchScore(p.Phase, phase) + MatchScore(p.Role, role))
            .ThenBy(p => string.IsNullOrWhiteSpace(p.Text) && p.Variants.Count == 0);

        return ranked.FirstOrDefault();
    }

    private AtcReadbackAcknowledgementTail? ResolveReadbackTail(string? phase, string? role)
    {
        var tails = _packs.MissingInfo.ReadbackTails;
        if (tails.Count == 0)
        {
            return null;
        }

        var ranked = tails
            .OrderByDescending(t => MatchScore(t.Phase, phase) + MatchScore(t.Role, role))
            .ThenBy(t => string.IsNullOrWhiteSpace(t.Text) && t.Variants.Count == 0);

        return ranked.FirstOrDefault(t => !string.IsNullOrWhiteSpace(t.Text) || t.Variants.Count > 0);
    }

    private static int MatchScore(string? candidate, string? value)
    {
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return 0;
        }

        return string.Equals(candidate, value, StringComparison.OrdinalIgnoreCase) ? 2 : -1;
    }

    private string? SelectText(string? text, IReadOnlyList<string> variants)
    {
        if (!string.IsNullOrWhiteSpace(text))
        {
            return text;
        }

        if (variants == null || variants.Count == 0)
        {
            return null;
        }

        return variants[_random.Next(variants.Count)];
    }
}

public sealed record AtcTemplateRequest(
    string Phase,
    string IntentId,
    string AtcAction,
    string? TemplateId);

public sealed record AtcTemplateRenderResult(
    string Text,
    bool RequiresReadback,
    IReadOnlyList<string> ReadbackItems);
