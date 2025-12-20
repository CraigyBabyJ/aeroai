using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading;
using AeroAI.Services;

namespace AeroAI.UI.Services;

/// <summary>
/// Applies deterministic, configurable text corrections to Whisper STT transcripts.
/// Supports hot reload of the JSON configuration.
/// </summary>
public sealed class SttCorrectionLayer : ISttCorrectionLayer, IDisposable
{
    private readonly Action<string>? _logger;
    private readonly object _sync = new();
    private readonly List<FileSystemWatcher> _watchers = new();
    private volatile RuleSet _ruleSet = RuleSet.Empty;
    private readonly string? _configPathOverride;
    private readonly bool _enableHotReload;

    private const string ConfigFileName = "stt_corrections.json";

    public SttCorrectionLayer(Action<string>? logger = null, string? configPathOverride = null, bool enableHotReload = true)
    {
        _logger = logger;
        _configPathOverride = configPathOverride;
        _enableHotReload = enableHotReload;

        ReloadRules();
        if (_enableHotReload)
            InitializeWatchers();
    }

    public string Apply(string transcript)
    {
        if (string.IsNullOrWhiteSpace(transcript))
            return transcript;

        var rules = _ruleSet;
        if (!rules.Enabled || rules.Rules.Count == 0)
            return transcript;

        var original = transcript;
        var current = transcript;
        var applied = new List<string>();

        foreach (var rule in rules.Rules)
        {
            if (!rule.Regex.IsMatch(current))
                continue;

            var updated = rule.Regex.Replace(current, rule.Replacement);
            if (!string.Equals(updated, current, StringComparison.Ordinal))
            {
                applied.Add(rule.Name);
                current = updated;
            }
        }

        if (applied.Count > 0)
        {
            _logger?.Invoke($"[STT-CORR] Applied: {string.Join("; ", applied)}; raw=\"{original}\" corrected=\"{current}\"");
        }

        return current;
    }

    public void Dispose()
    {
        lock (_sync)
        {
            foreach (var watcher in _watchers)
            {
                try { watcher.Dispose(); } catch { /* ignore */ }
            }
            _watchers.Clear();
        }
    }

    private void InitializeWatchers()
    {
        var candidateDirectories = GetCandidatePaths()
            .Select(Path.GetDirectoryName)
            .Where(d => !string.IsNullOrWhiteSpace(d) && Directory.Exists(d))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var dir in candidateDirectories)
        {
            try
            {
                var watcher = new FileSystemWatcher(dir, ConfigFileName)
                {
                    NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size | NotifyFilters.CreationTime
                };

                watcher.Changed += OnConfigChanged;
                watcher.Created += OnConfigChanged;
                watcher.Renamed += OnConfigChanged;
                watcher.Deleted += OnConfigChanged;
                watcher.EnableRaisingEvents = true;

                _watchers.Add(watcher);
            }
            catch (Exception ex)
            {
                _logger?.Invoke($"[STT corrections] Failed to watch '{dir}': {ex.Message}");
            }
        }
    }

    private void OnConfigChanged(object? sender, FileSystemEventArgs e)
    {
        // Debounce bursts by reloading after a short delay.
        ThreadPool.QueueUserWorkItem(_ =>
        {
            try
            {
                Thread.Sleep(150);
                ReloadRules();
            }
            catch
            {
                // ignore
            }
        });
    }

    private void ReloadRules()
    {
        RuleSet previous = _ruleSet;

        try
        {
            var path = ResolveConfigPath();
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                _ruleSet = RuleSet.Disabled;
                _logger?.Invoke("[STT corrections] Config not found; corrections disabled.");
                return;
            }

            var json = File.ReadAllText(path);
            var config = JsonSerializer.Deserialize<SttCorrectionsConfig>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (config?.Enabled != true || config.Rules is not { Count: > 0 })
            {
                _ruleSet = RuleSet.Disabled;
                _logger?.Invoke("[STT corrections] Config disabled or contains no rules; corrections disabled.");
                return;
            }

            var compiled = new List<CompiledRule>();
            foreach (var rule in config.Rules)
            {
                if (!string.Equals(rule.Type, "regex", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (string.IsNullOrWhiteSpace(rule.Pattern))
                    continue;

                try
                {
                    var options = RegexOptions.Compiled | RegexOptions.CultureInvariant;
                    if (rule.IgnoreCase)
                        options |= RegexOptions.IgnoreCase;

                    var regex = new Regex(rule.Pattern, options);
                    compiled.Add(new CompiledRule(rule.Name ?? rule.Pattern, regex, rule.Replacement ?? string.Empty));
                }
                catch (Exception ex)
                {
                    _logger?.Invoke($"[STT corrections] Skipped rule '{rule.Name ?? rule.Pattern}': {ex.Message}");
                }
            }

            _ruleSet = compiled.Count == 0
                ? RuleSet.Disabled
                : new RuleSet(true, compiled, path);

            _logger?.Invoke($"[STT corrections] Loaded {_ruleSet.Rules.Count} rule(s) from '{path}'.");
        }
        catch (Exception ex)
        {
            _ruleSet = previous;
            _logger?.Invoke($"[STT corrections] Reload failed, keeping previous rules: {ex.Message}");
        }
    }

    private string? ResolveConfigPath()
    {
        foreach (var candidate in GetCandidatePaths())
        {
            if (File.Exists(candidate))
                return candidate;
        }

        return null;
    }

    private IEnumerable<string> GetCandidatePaths()
    {
        if (!string.IsNullOrWhiteSpace(_configPathOverride))
        {
            yield return _configPathOverride;
        }

        var baseDir = AppContext.BaseDirectory;
        yield return Path.Combine(baseDir, ConfigFileName); // same directory as executable

        for (var i = 0; i < 6; i++)
        {
            var prefix = Enumerable.Repeat("..", i).ToArray();
            var candidate = Path.GetFullPath(Path.Combine(new[] { baseDir }.Concat(prefix).Concat(new[] { "Config", ConfigFileName }).ToArray()));
            yield return candidate;
        }
    }

    private sealed record RuleSet(bool Enabled, IReadOnlyList<CompiledRule> Rules, string? SourcePath)
    {
        public static readonly RuleSet Empty = new(false, Array.Empty<CompiledRule>(), null);
        public static readonly RuleSet Disabled = new(false, Array.Empty<CompiledRule>(), null);
    }

    private sealed record SttCorrectionsConfig
    {
        [JsonPropertyName("enabled")]
        public bool Enabled { get; init; }

        [JsonPropertyName("rules")]
        public List<SttCorrectionRule>? Rules { get; init; }
    }

    private sealed record SttCorrectionRule
    {
        [JsonPropertyName("name")]
        public string? Name { get; init; }

        [JsonPropertyName("type")]
        public string? Type { get; init; }

        [JsonPropertyName("pattern")]
        public string? Pattern { get; init; }

        [JsonPropertyName("replacement")]
        public string? Replacement { get; init; }

        [JsonPropertyName("ignoreCase")]
        public bool IgnoreCase { get; init; } = true;
    }

    private sealed record CompiledRule(string Name, Regex Regex, string Replacement);

#if DEBUG
    internal void ForceReloadForTests()
    {
        ReloadRules();
    }
#endif
}
