using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using AeroAI.Atc;

namespace AeroAI.Data;

/// <summary>
/// Resolves airport names from flight context or the canonical airports dataset.
/// </summary>
public static class AirportNameResolver
{
	private static readonly Lazy<Dictionary<string, string>> NamesByIcao = new Lazy<Dictionary<string, string>>(LoadAirportNames, isThreadSafe: true);

	/// <summary>
	/// Resolve a friendly airport name for the given ICAO using, in order:
	/// 1) SimBrief/flight context-sourced names, 2) airports dataset, 3) ICAO (fallback).
	/// </summary>
	public static string ResolveAirportName(string? icao, FlightContext? flightContext)
	{
		if (string.IsNullOrWhiteSpace(icao))
			return string.Empty;

		var upper = icao.Trim().ToUpperInvariant();

		if (flightContext != null)
		{
			if (!string.IsNullOrWhiteSpace(flightContext.OriginIcao) &&
			    upper.Equals(flightContext.OriginIcao.Trim().ToUpperInvariant(), StringComparison.OrdinalIgnoreCase) &&
			    !string.IsNullOrWhiteSpace(flightContext.OriginName))
			{
				return flightContext.OriginName!;
			}

			if (!string.IsNullOrWhiteSpace(flightContext.DestinationIcao) &&
			    upper.Equals(flightContext.DestinationIcao.Trim().ToUpperInvariant(), StringComparison.OrdinalIgnoreCase) &&
			    !string.IsNullOrWhiteSpace(flightContext.DestinationName))
			{
				return flightContext.DestinationName!;
			}
		}

		if (NamesByIcao.Value.TryGetValue(upper, out var name) && !string.IsNullOrWhiteSpace(name))
			return name;

		// Fallback: return ICAO (internal use only; phrasing layer should avoid speaking it).
		return upper;
	}

	/// <summary>
	/// Returns true if the token is a known airport ICAO in the dataset.
	/// </summary>
	public static bool IsKnownAirportIcao(string? token)
	{
		if (string.IsNullOrWhiteSpace(token) || token.Length != 4)
			return false;

		return NamesByIcao.Value.ContainsKey(token.Trim().ToUpperInvariant());
	}

	/// <summary>
	/// Attempts to resolve an airport name (e.g., "Calgary") to an ICAO code (e.g., "CYYC").
	/// Uses fuzzy matching on airport names and municipalities in the airports dataset.
	/// Returns null if no match is found.
	/// </summary>
	public static string? ResolveIcaoFromName(string? airportName, FlightContext? flightContext = null)
	{
		if (string.IsNullOrWhiteSpace(airportName))
			return null;

		var normalizedName = airportName.Trim();
		
		// If it's already an ICAO code, return it
		if (normalizedName.Length == 4 && IsKnownAirportIcao(normalizedName))
			return normalizedName.ToUpperInvariant();

		// Try to find in airports.json by matching name or municipality
		string? path = ResolveDataPath();
		if (path == null || !File.Exists(path))
			return null;

		try
		{
			using var doc = JsonDocument.Parse(File.ReadAllText(path));
			foreach (var airport in doc.RootElement.EnumerateObject())
			{
				var icao = airport.Name?.Trim();
				if (string.IsNullOrWhiteSpace(icao))
					continue;

				// Check municipality
				if (airport.Value.TryGetProperty("municipality", out var muniProp))
				{
					var muni = muniProp.GetString();
					if (!string.IsNullOrWhiteSpace(muni) && 
					    muni.Trim().Equals(normalizedName, StringComparison.OrdinalIgnoreCase))
					{
						return icao.ToUpperInvariant();
					}
				}

				// Check full airport name
				if (airport.Value.TryGetProperty("name", out var nameProp))
				{
					var name = nameProp.GetString();
					if (!string.IsNullOrWhiteSpace(name))
					{
						// Exact match
						if (name.Trim().Equals(normalizedName, StringComparison.OrdinalIgnoreCase))
							return icao.ToUpperInvariant();
						
						// Contains match (e.g., "Calgary International" contains "Calgary")
						if (name.Contains(normalizedName, StringComparison.OrdinalIgnoreCase) ||
						    normalizedName.Contains(name.Trim(), StringComparison.OrdinalIgnoreCase))
						{
							return icao.ToUpperInvariant();
						}
					}
				}
			}
		}
		catch (JsonException)
		{
			// Ignore
		}
		catch (IOException)
		{
			// Ignore
		}

		return null;
	}

	private static Dictionary<string, string> LoadAirportNames()
	{
		var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		string? path = ResolveDataPath();
		if (path == null || !File.Exists(path))
			return map;

		try
		{
			using var doc = JsonDocument.Parse(File.ReadAllText(path));
			foreach (var airport in doc.RootElement.EnumerateObject())
			{
				var key = airport.Name?.Trim();
				if (string.IsNullOrWhiteSpace(key))
					continue;

				string? name = null;

				// Prefer municipality for ATC speech
				if (airport.Value.TryGetProperty("municipality", out var muniProp))
				{
					name = muniProp.GetString();
				}

				// Fallback to full airport name
				if (string.IsNullOrWhiteSpace(name) && airport.Value.TryGetProperty("name", out var nameProp))
				{
					name = nameProp.GetString();
				}

				if (!string.IsNullOrWhiteSpace(name))
				{
					map[key.ToUpperInvariant()] = name.Trim();
				}
			}
		}
		catch (JsonException)
		{
			// Ignore and leave map empty
		}
		catch (IOException)
		{
			// Ignore and leave map empty
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
