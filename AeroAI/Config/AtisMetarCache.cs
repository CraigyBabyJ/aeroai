using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace AeroAI.Config;

public static class AtisMetarCache
{
    private static readonly object Sync = new();
    private static Dictionary<string, Entry>? _cache;

    public static AtisMetarSnapshot Get(string airportIcao)
    {
        if (string.IsNullOrWhiteSpace(airportIcao))
            return new AtisMetarSnapshot(null, null);

        var key = airportIcao.Trim().ToUpperInvariant();
        lock (Sync)
        {
            EnsureLoaded();
            if (_cache!.TryGetValue(key, out var entry))
                return new AtisMetarSnapshot(entry.AtisLetter, entry.RawMetar);
            return new AtisMetarSnapshot(null, null);
        }
    }

    public static AtisMetarSnapshot UpdateMetar(string airportIcao, string? rawMetar)
    {
        if (string.IsNullOrWhiteSpace(airportIcao))
            return new AtisMetarSnapshot(null, null);

        var key = airportIcao.Trim().ToUpperInvariant();
        var metar = string.IsNullOrWhiteSpace(rawMetar) ? null : rawMetar.Trim();

        lock (Sync)
        {
            EnsureLoaded();
            if (!_cache!.TryGetValue(key, out var entry))
            {
                entry = new Entry { AtisLetter = "A", RawMetar = metar, UpdatedUtc = DateTime.UtcNow };
                _cache[key] = entry;
                Save();
                return new AtisMetarSnapshot(entry.AtisLetter, entry.RawMetar);
            }

            if (!string.IsNullOrWhiteSpace(metar) && !string.Equals(entry.RawMetar, metar, StringComparison.Ordinal))
            {
                entry.AtisLetter = NextLetter(entry.AtisLetter);
                entry.RawMetar = metar;
                entry.UpdatedUtc = DateTime.UtcNow;
                Save();
            }

            return new AtisMetarSnapshot(entry.AtisLetter, entry.RawMetar);
        }
    }

    public static void SetAtisLetter(string airportIcao, string letter)
    {
        if (string.IsNullOrWhiteSpace(airportIcao) || string.IsNullOrWhiteSpace(letter))
            return;

        var key = airportIcao.Trim().ToUpperInvariant();
        var normalized = letter.Trim().ToUpperInvariant();
        if (normalized.Length != 1 || normalized[0] < 'A' || normalized[0] > 'Z')
            return;

        lock (Sync)
        {
            EnsureLoaded();
            if (!_cache!.TryGetValue(key, out var entry))
            {
                entry = new Entry { AtisLetter = normalized, RawMetar = null, UpdatedUtc = DateTime.UtcNow };
                _cache[key] = entry;
            }
            else
            {
                entry.AtisLetter = normalized;
                entry.UpdatedUtc = DateTime.UtcNow;
            }

            Save();
        }
    }

    private static string NextLetter(string? current)
    {
        var c = string.IsNullOrWhiteSpace(current) ? 'A' : char.ToUpperInvariant(current.Trim()[0]);
        if (c < 'A' || c > 'Z') c = 'A';
        c = c == 'Z' ? 'A' : (char)(c + 1);
        return c.ToString();
    }

    private static void EnsureLoaded()
    {
        if (_cache != null)
            return;

        try
        {
            var path = GetPath();
            if (File.Exists(path))
            {
                var json = File.ReadAllText(path);
                _cache = JsonSerializer.Deserialize<Dictionary<string, Entry>>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                }) ?? new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);
            }
            else
            {
                _cache = new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);
            }
        }
        catch
        {
            _cache = new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private static void Save()
    {
        try
        {
            var path = GetPath();
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(dir))
                Directory.CreateDirectory(dir);

            var json = JsonSerializer.Serialize(_cache, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(path, json);
        }
        catch
        {
            // ignore persistence failures
        }
    }

    private static string GetPath()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return Path.Combine(appData, "AeroAI", "cache", "atis_metar_cache.json");
    }

    private sealed class Entry
    {
        public string? AtisLetter { get; set; }
        public string? RawMetar { get; set; }
        public DateTime UpdatedUtc { get; set; }
    }
}

public readonly record struct AtisMetarSnapshot(string? AtisLetter, string? RawMetar);
