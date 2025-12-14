using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using AeroAI.Atc;

namespace AtcNavDataDemo.Config;

public class AudioEffectsProfile
{
	[JsonPropertyName("enabled")]
	public bool Enabled { get; set; } = false;

	[JsonPropertyName("bandpassLowHz")]
	public int BandpassLowHz { get; set; } = 300;

	[JsonPropertyName("bandpassHighHz")]
	public int BandpassHighHz { get; set; } = 3200;

	/// <summary>
	/// 0..1 noise mix.
	/// </summary>
	[JsonPropertyName("noiseLevel")]
	public double NoiseLevel { get; set; } = 0.0;

	/// <summary>
	/// 0..1 gentle compression/drive amount.
	/// </summary>
	[JsonPropertyName("compressionAmount")]
	public double CompressionAmount { get; set; } = 0.0;

	/// <summary>
	/// 0 = dry, 1 = fully effected.
	/// </summary>
	[JsonPropertyName("dryWetMix")]
	public double DryWetMix { get; set; } = 1.0;

	[JsonPropertyName("squelchTailFile")]
	public string? SquelchTailFile { get; set; }

	[JsonPropertyName("squelchTailGainDb")]
	public double SquelchTailGainDb { get; set; } = 0.0;
}

public class AudioEffectsConfig
{
	[JsonPropertyName("enabled")]
	public bool Enabled { get; set; } = true;

	[JsonPropertyName("profiles")]
	public Dictionary<string, AudioEffectsProfile> Profiles { get; set; } = new(StringComparer.OrdinalIgnoreCase);

	// TODO: When adding distance-aware effects, extend profiles or add a per-unit
	// distance curve, consuming SimConnect positions to modulate NoiseLevel/DryWetMix.
}

public static class AudioEffectsConfigStore
{
	private static readonly Lazy<AudioEffectsConfig> _current = new(LoadInternal, isThreadSafe: true);

	public static AudioEffectsConfig Current => _current.Value;

	public static AudioEffectsProfile? GetProfileForUnit(AtcUnit unit)
	{
		var cfg = Current;
		if (!cfg.Enabled)
			return null;

		if (cfg.Profiles.TryGetValue(unit.ToString(), out var profile) && profile.Enabled)
			return profile;

		return null;
	}

	private static AudioEffectsConfig LoadInternal()
	{
		const string configPath = "Config/audio-effects.json";

		if (!File.Exists(configPath))
			return new AudioEffectsConfig();

		try
		{
			var json = File.ReadAllText(configPath);
			var options = new JsonSerializerOptions
			{
				PropertyNameCaseInsensitive = true
			};
			var cfg = JsonSerializer.Deserialize<AudioEffectsConfig>(json, options);
			return cfg ?? new AudioEffectsConfig();
		}
		catch
		{
			return new AudioEffectsConfig();
		}
	}
}
