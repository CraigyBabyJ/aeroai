using System;
using System.IO;
using System.Text.Json;

namespace AeroAI.AtcSession;

public sealed class AtcJsonPackLoader
{
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public AtcPackStore? TryLoadAll(Action<string>? onDebug = null)
    {
        var intentsPath = ResolveDataPath("Data", "atc", "intents.json");
        var flowsPath = ResolveDataPath("Data", "atc", "flows.json");
        var templatesPath = ResolveDataPath("Data", "atc", "templates.json");

        if (intentsPath == null || flowsPath == null || templatesPath == null)
        {
            onDebug?.Invoke("[ATC JSON] Pack files not found; JSON session layer disabled.");
            return null;
        }

        try
        {
            var intents = Deserialize<AtcIntentPack>(intentsPath);
            var flows = Deserialize<AtcFlowPack>(flowsPath);
            var templates = Deserialize<AtcTemplatePack>(templatesPath);

            if (intents == null || flows == null || templates == null)
            {
                onDebug?.Invoke("[ATC JSON] Failed to parse packs; JSON session layer disabled.");
                return null;
            }

            return new AtcPackStore(intents, flows, templates);
        }
        catch (Exception ex)
        {
            onDebug?.Invoke($"[ATC JSON] Pack load failed: {ex.Message}");
            return null;
        }
    }

    private T? Deserialize<T>(string path)
    {
        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<T>(json, _jsonOptions);
    }

    private static string? ResolveDataPath(params string[] segments)
    {
        var relative = Path.Combine(segments);
        var baseDir = AppContext.BaseDirectory;

        var candidates = new[]
        {
            Path.Combine(baseDir, relative),
            Path.Combine(baseDir, "..", "..", "..", relative),
            Path.Combine(baseDir, "..", "..", "..", "..", relative),
            Path.Combine(Directory.GetCurrentDirectory(), relative)
        };

        foreach (var candidate in candidates)
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }
}
