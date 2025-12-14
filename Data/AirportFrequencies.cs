using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace AeroAI.Data;

/// <summary>
/// JSON-backed lookup for airport frequencies. Provides quick access to
/// clearance, ground, tower, departure, and approach frequencies by ICAO code.
/// </summary>
public static class AirportFrequencies
{
	private static readonly Lazy<Dictionary<string, AirportFrequencySet>> AllByIcao = new Lazy<Dictionary<string, AirportFrequencySet>>(LoadAllFrequencies, isThreadSafe: true);
	private static readonly Lazy<Dictionary<string, double>> GroundByIcao = new Lazy<Dictionary<string, double>>(LoadGroundFrequencies, isThreadSafe: true);

	/// <summary>
	/// Try to fetch a ground frequency (MHz) for the given ICAO.
	/// </summary>
	public static bool TryGetGroundFrequency(string? icao, out double frequencyMhz)
	{
		frequencyMhz = 0;
		if (string.IsNullOrWhiteSpace(icao))
		{
			return false;
		}
		return GroundByIcao.Value.TryGetValue(icao.Trim(), out frequencyMhz);
	}

	/// <summary>
	/// Try to fetch common frequencies (clearance, ground, tower, departure, approach) for the given ICAO.
	/// </summary>
	public static bool TryGetFrequencies(string? icao, out AirportFrequencySet frequencies)
	{
		frequencies = new AirportFrequencySet();
		if (string.IsNullOrWhiteSpace(icao))
		{
			return false;
		}

		return AllByIcao.Value.TryGetValue(icao.Trim(), out frequencies);
	}

	private static Dictionary<string, double> LoadGroundFrequencies()
	{
		Dictionary<string, double> map = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
		string? path = ResolveDataPath();
		if (path == null || !File.Exists(path))
		{
			return map;
		}

		try
		{
			string jsonContent = File.ReadAllText(path);
			var jsonData = JsonSerializer.Deserialize<Dictionary<string, AirportFrequencyJson>>(jsonContent);
			
			if (jsonData != null)
			{
				foreach (var kvp in jsonData)
				{
					if (kvp.Value.Ground.HasValue)
					{
						map[kvp.Key] = kvp.Value.Ground.Value;
					}
				}
			}
		}
		catch (JsonException ex)
		{
			System.Diagnostics.Debug.WriteLine($"Failed to load airport frequencies JSON: {ex.Message}");
		}
		catch (IOException ex)
		{
			System.Diagnostics.Debug.WriteLine($"Failed to read airport frequencies file: {ex.Message}");
		}
		
		return map;
	}

	private static Dictionary<string, AirportFrequencySet> LoadAllFrequencies()
	{
		Dictionary<string, AirportFrequencySet> map = new Dictionary<string, AirportFrequencySet>(StringComparer.OrdinalIgnoreCase);
		string? path = ResolveDataPath();
		if (path == null || !File.Exists(path))
		{
			return map;
		}

		try
		{
			string jsonContent = File.ReadAllText(path);
			var jsonData = JsonSerializer.Deserialize<Dictionary<string, AirportFrequencyJson>>(jsonContent);
			
			if (jsonData != null)
			{
				foreach (var kvp in jsonData)
				{
					var jsonFreq = kvp.Value;
					var freqSet = new AirportFrequencySet
					{
						Clearance = jsonFreq.Clearance,
						Ground = jsonFreq.Ground,
						Tower = jsonFreq.Tower,
						Departure = jsonFreq.Departure,
						Approach = jsonFreq.Approach
					};
					map[kvp.Key] = freqSet;
				}
			}
		}
		catch (JsonException ex)
		{
			// Log error if needed, but return empty map for graceful degradation
			System.Diagnostics.Debug.WriteLine($"Failed to load airport frequencies JSON: {ex.Message}");
		}
		catch (IOException ex)
		{
			System.Diagnostics.Debug.WriteLine($"Failed to read airport frequencies file: {ex.Message}");
		}

		return map;
	}

	private static string? ResolveDataPath()
	{
		// First try relative to the executable base directory.
		string baseDir = AppContext.BaseDirectory;
		string candidate = Path.Combine(baseDir, "Data", "airport-frequencies.json");
		if (File.Exists(candidate))
		{
			return candidate;
		}

		// Fallback: project root relative (bin/Debug/../..).
		string fallback = Path.Combine(baseDir, "..", "..", "..", "Data", "airport-frequencies.json");
		if (File.Exists(fallback))
		{
			return fallback;
		}

		return null;
	}
}

public sealed class AirportFrequencySet
{
	public double? Clearance { get; set; }
	public double? Ground { get; set; }
	public double? Tower { get; set; }
	public double? Departure { get; set; }
	public double? Approach { get; set; }
}

/// <summary>
/// JSON deserialization model for airport frequency data.
/// </summary>
internal sealed class AirportFrequencyJson
{
	public double? Clearance { get; set; }
	public double? Ground { get; set; }
	public double? Tower { get; set; }
	public double? Departure { get; set; }
	public double? Approach { get; set; }
}

