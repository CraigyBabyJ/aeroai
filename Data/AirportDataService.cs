using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace AeroAI.Data;

public sealed class AirportInfo
{
    public string Icao { get; init; } = string.Empty;
    public string? IsoCountry { get; init; }
    public string? IsoRegion { get; init; }
}

public sealed class AirportRegionHints
{
    public string? IsoCountry { get; init; }
    public string? IsoRegion { get; init; }
    public string? RegionPrefix { get; init; }
}

public static class AirportDataService
{
    private static readonly Lazy<Dictionary<string, AirportInfo>> AirportsByIcao =
        new Lazy<Dictionary<string, AirportInfo>>(LoadAirports, isThreadSafe: true);

    public static bool TryGetAirportInfo(string? icao, out AirportInfo info)
    {
        info = new AirportInfo();
        if (string.IsNullOrWhiteSpace(icao))
            return false;

        var key = icao.Trim().ToUpperInvariant();
        return AirportsByIcao.Value.TryGetValue(key, out info);
    }

    public static AirportRegionHints GetRegionHints(string? icao)
    {
        var hints = new AirportRegionHints
        {
            RegionPrefix = string.IsNullOrWhiteSpace(icao) ? null : icao.Trim().ToUpperInvariant().Substring(0, Math.Min(2, icao.Trim().Length))
        };

        if (!TryGetAirportInfo(icao, out var info))
            return hints;

        return new AirportRegionHints
        {
            IsoCountry = info.IsoCountry,
            IsoRegion = info.IsoRegion,
            RegionPrefix = hints.RegionPrefix
        };
    }

    private static Dictionary<string, AirportInfo> LoadAirports()
    {
        var map = new Dictionary<string, AirportInfo>(StringComparer.OrdinalIgnoreCase);
        var path = ResolveDataPath();
        if (path == null || !File.Exists(path))
            return map;

        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            foreach (var airport in doc.RootElement.EnumerateObject())
            {
                var icao = airport.Name?.Trim();
                if (string.IsNullOrWhiteSpace(icao))
                    continue;

                string? isoCountry = null;
                string? isoRegion = null;

                if (airport.Value.TryGetProperty("iso_country", out var countryProp))
                    isoCountry = countryProp.GetString();
                if (airport.Value.TryGetProperty("iso_region", out var regionProp))
                    isoRegion = regionProp.GetString();

                map[icao.ToUpperInvariant()] = new AirportInfo
                {
                    Icao = icao.ToUpperInvariant(),
                    IsoCountry = isoCountry,
                    IsoRegion = isoRegion
                };
            }
        }
        catch (JsonException)
        {
            // ignore
        }
        catch (IOException)
        {
            // ignore
        }

        return map;
    }

    private static string? ResolveDataPath()
    {
        string baseDir = AppContext.BaseDirectory;
        string[] candidates =
        {
            Path.Combine(baseDir, "Data", "airports.json"),
            Path.Combine(baseDir, "..", "..", "..", "Data", "airports.json"),
            Path.Combine(baseDir, "..", "..", "..", "..", "Data", "airports.json"),
            Path.Combine(Directory.GetCurrentDirectory(), "Data", "airports.json")
        };

        foreach (var candidate in candidates)
        {
            if (File.Exists(candidate))
                return candidate;
        }

        return null;
    }
}
