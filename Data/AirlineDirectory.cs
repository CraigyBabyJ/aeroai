using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AeroAI.Data;

public sealed class AirlineInfo
{
	public string Icao { get; set; } = string.Empty;

	[JsonPropertyName("name")]
	public string Name { get; set; } = string.Empty;

	[JsonPropertyName("call_sign")]
	public string CallSign { get; set; } = string.Empty;

	public string GetPreferredDisplay()
	{
		return string.IsNullOrWhiteSpace(CallSign) ? Name : CallSign;
	}
}

public sealed class AirlineDirectory
{
	private static readonly string DefaultRelativePath = Path.Combine("Data", "airlines.json");

	private readonly Dictionary<string, AirlineInfo> _airlines;

	public AirlineDirectory(Dictionary<string, AirlineInfo> airlines, string sourcePath)
	{
		_airlines = airlines;
		SourcePath = sourcePath;
	}

	public string SourcePath { get; }

	public bool HasData => _airlines.Count > 0;

	public static AirlineDirectory Load(string? path = null)
	{
		var resolvedPath = ResolvePath(path);

		if (!File.Exists(resolvedPath))
		{
			Console.WriteLine($"[CALLSIGN] airlines.json not found at {resolvedPath}, continuing without airline names.");
			return new AirlineDirectory(new Dictionary<string, AirlineInfo>(StringComparer.OrdinalIgnoreCase), resolvedPath);
		}

		try
		{
			var json = File.ReadAllText(resolvedPath);
			var root = JsonSerializer.Deserialize<AirlinesFile>(json, new JsonSerializerOptions
			{
				PropertyNameCaseInsensitive = true
			});

			Dictionary<string, AirlineInfo> airlines = root?.Airlines ?? new Dictionary<string, AirlineInfo>(StringComparer.OrdinalIgnoreCase);
			foreach (var kvp in airlines)
			{
				if (string.IsNullOrWhiteSpace(kvp.Value.Icao))
				{
					kvp.Value.Icao = kvp.Key.ToUpperInvariant();
				}
			}

			return new AirlineDirectory(new Dictionary<string, AirlineInfo>(airlines, StringComparer.OrdinalIgnoreCase), resolvedPath);
		}
		catch (Exception ex)
		{
			Console.WriteLine($"[CALLSIGN] Failed to load airlines.json at {resolvedPath}: {ex.Message}");
			return new AirlineDirectory(new Dictionary<string, AirlineInfo>(StringComparer.OrdinalIgnoreCase), resolvedPath);
		}
	}

	public bool TryGetAirline(string airlineIcao, [NotNullWhen(true)] out AirlineInfo? info)
	{
		return _airlines.TryGetValue(airlineIcao.ToUpperInvariant(), out info);
	}

	private static string ResolvePath(string? path)
	{
		if (!string.IsNullOrWhiteSpace(path))
		{
			return path;
		}

		if (File.Exists(DefaultRelativePath))
		{
			return DefaultRelativePath;
		}

		var baseDirPath = Path.Combine(AppContext.BaseDirectory ?? string.Empty, "Data", "airlines.json");
		if (File.Exists(baseDirPath))
		{
			return baseDirPath;
		}

		return DefaultRelativePath;
	}

	private sealed class AirlinesFile
	{
		[JsonPropertyName("airlines")]
		public Dictionary<string, AirlineInfo> Airlines { get; set; } = new(StringComparer.OrdinalIgnoreCase);
	}
}
