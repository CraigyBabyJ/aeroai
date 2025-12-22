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
