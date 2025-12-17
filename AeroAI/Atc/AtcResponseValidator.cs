using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using AeroAI.Data;

namespace AeroAI.Atc;

public sealed class AtcResponseValidationResult
{
	public bool IsValid { get; set; }
	public List<string> Reasons { get; } = new();
	public List<string> OffendingTokens { get; } = new();
}

/// <summary>
/// Defensive validator that catches LLM responses which invent operational data not present in the ATC/flight context.
/// </summary>
public static class AtcResponseValidator
{
	private static readonly Regex RunwayRegex = new(@"\b(?:RUNWAY|RWY)\s*0?(?<rw>\d{1,2}[LRC]?)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
	private static readonly Regex SquawkRegex = new(@"\bSQUAWK\s+(?<sq>\d{4})\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
	private static readonly Regex FrequencyRegex = new(@"\b(?<freq>1[1-3]\d\.\d{1,3})\b", RegexOptions.Compiled);
	private static readonly Regex SidRegex = new(@"\b(?:SID|DEPARTURE)\s+(?<sid>[A-Z0-9][A-Z0-9\.\-]{2,8})\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
	private static readonly Regex ViaSidRegex = new(@"\bVIA\s+(?<sid>[A-Z0-9][A-Z0-9\.\-]{2,8})\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
	private static readonly Regex FlightLevelRegex = new(@"\bFL\s*(?<fl>\d{2,3})\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
	private static readonly Regex AltitudeFtRegex = new(@"\b(?<alt>\d{3,5})\s*(?:FT|FEET)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
	private static readonly Regex AltitudeVerbRegex = new(@"\b(?:CLIMB|MAINTAIN|DESCEND|ALTITUDE|LEVEL|INITIAL CLIMB)\s+(?<alt>\d{3,5})\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
	private static readonly Regex DestinationIcaoRegex = new(@"\b(?<icao>[A-Z]{4})\b", RegexOptions.Compiled);

	public static AtcResponseValidationResult Validate(string llmText, AtcContext atcContext, FlightContext flightContext)
	{
		var result = new AtcResponseValidationResult();

		if (string.IsNullOrWhiteSpace(llmText))
		{
			result.Reasons.Add("LLM response was empty");
			return result;
		}

		if (atcContext == null || flightContext == null)
		{
			result.IsValid = true; // Nothing to compare against; do not block.
			return result;
		}

		var normalized = ReadbackNormalizer.Normalize(llmText, flightContext);
		var text = normalized.ToUpperInvariant();

		ValidateRunway(text, atcContext, flightContext, result);
		ValidateSquawk(text, atcContext, flightContext, result);
		ValidateAltitude(text, atcContext, flightContext, result);
		ValidateFrequency(text, atcContext, flightContext, result);
		ValidateSid(text, atcContext, flightContext, result);
		ValidateAirportIcaoSpeech(text, atcContext, flightContext, result);

		result.IsValid = result.Reasons.Count == 0;
		return result;
	}

	private static void ValidateRunway(string text, AtcContext ctx, FlightContext flightCtx, AtcResponseValidationResult result)
	{
		var mentions = ExtractRunways(text);
		if (mentions.Count == 0)
			return;

		var allowed = GetAllowedRunways(ctx, flightCtx);
		if (allowed.Count == 0)
		{
			result.Reasons.Add("Runway issued but no runway exists in context");
			result.OffendingTokens.AddRange(mentions);
			return;
		}

		foreach (var rw in mentions)
		{
			if (!allowed.Contains(rw))
			{
				result.Reasons.Add($"Runway '{rw}' not in context");
				result.OffendingTokens.Add(rw);
			}
		}
	}

	private static void ValidateSquawk(string text, AtcContext ctx, FlightContext flightCtx, AtcResponseValidationResult result)
	{
		var mentions = ExtractSquawks(text);
		if (mentions.Count == 0)
			return;

		var allowed = GetAllowedSquawks(ctx, flightCtx);
		if (allowed.Count == 0)
		{
			result.Reasons.Add("Squawk issued but none assigned in context");
			result.OffendingTokens.AddRange(mentions);
			return;
		}

		foreach (var sq in mentions)
		{
			if (!allowed.Contains(sq, StringComparer.OrdinalIgnoreCase))
			{
				result.Reasons.Add($"Squawk '{sq}' not in context");
				result.OffendingTokens.Add(sq);
			}
		}
	}

	private static void ValidateAltitude(string text, AtcContext ctx, FlightContext flightCtx, AtcResponseValidationResult result)
	{
		var mentions = ExtractAltitudes(text);
		if (mentions.Count == 0)
			return;

		var allowed = GetAllowedAltitudes(ctx, flightCtx);
		if (allowed.Count == 0)
		{
			result.Reasons.Add("Altitude issued but none assigned in context");
			result.OffendingTokens.AddRange(mentions.Select(m => m.Raw));
			return;
		}

		foreach (var alt in mentions)
		{
			if (!allowed.Contains(alt.Feet))
			{
				result.Reasons.Add($"Altitude '{alt.Raw}' not in context");
				result.OffendingTokens.Add(alt.Raw);
			}
		}
	}

	private static void ValidateFrequency(string text, AtcContext ctx, FlightContext flightCtx, AtcResponseValidationResult result)
	{
		var mentions = ExtractFrequencies(text);
		if (mentions.Count == 0)
			return;

		var allowed = GetAllowedFrequencies(ctx, flightCtx);
		if (allowed.Count == 0)
		{
			result.Reasons.Add("Frequency issued but none available in context");
			result.OffendingTokens.AddRange(mentions.Select(m => m.Raw));
			return;
		}

		foreach (var freq in mentions)
		{
			if (!allowed.Any(a => Math.Abs(a - freq.ValueMhz) < 0.005))
			{
				result.Reasons.Add($"Frequency '{freq.Raw}' not in context");
				result.OffendingTokens.Add(freq.Raw);
			}
		}
	}

	private static void ValidateSid(string text, AtcContext ctx, FlightContext flightCtx, AtcResponseValidationResult result)
	{
		var mentions = ExtractSids(text);
		if (mentions.Count == 0)
			return;

		var allowed = GetAllowedSids(ctx, flightCtx);
		if (allowed.Count == 0)
		{
			result.Reasons.Add("SID issued but none assigned in context");
			result.OffendingTokens.AddRange(mentions);
			return;
		}

		foreach (var sid in mentions)
		{
			if (!allowed.Contains(sid))
			{
				result.Reasons.Add($"SID '{sid}' not in context");
				result.OffendingTokens.Add(sid);
			}
		}
	}

	private static void ValidateAirportIcaoSpeech(string text, AtcContext ctx, FlightContext flightCtx, AtcResponseValidationResult result)
	{
		var originIcao = flightCtx?.OriginIcao?.Trim().ToUpperInvariant();
		var destIcao = flightCtx?.DestinationIcao?.Trim().ToUpperInvariant();

		var tokens = ExtractPotentialIcaos(text);
		var offending = new List<string>();

		foreach (var token in tokens)
		{
			if (AirportNameResolver.IsKnownAirportIcao(token) ||
			    (!string.IsNullOrWhiteSpace(originIcao) && token.Equals(originIcao, StringComparison.OrdinalIgnoreCase)) ||
			    (!string.IsNullOrWhiteSpace(destIcao) && token.Equals(destIcao, StringComparison.OrdinalIgnoreCase)))
			{
				offending.Add(token);
			}
		}

		if (offending.Count > 0)
		{
			result.Reasons.Add("Airport ICAO spoken in ATC transmission");
			result.OffendingTokens.AddRange(offending.Distinct(StringComparer.OrdinalIgnoreCase));
		}
	}

	private static List<string> ExtractRunways(string text)
	{
		return RunwayRegex.Matches(text)
			.Select(m => NormalizeRunway(m.Groups["rw"].Value))
			.Where(r => !string.IsNullOrWhiteSpace(r))
			.Distinct(StringComparer.OrdinalIgnoreCase)
			.ToList();
	}

	private static List<string> ExtractSquawks(string text)
	{
		return SquawkRegex.Matches(text)
			.Select(m => m.Groups["sq"].Value)
			.Where(s => !string.IsNullOrWhiteSpace(s))
			.Distinct(StringComparer.OrdinalIgnoreCase)
			.ToList();
	}

	private static List<AltitudeMention> ExtractAltitudes(string text)
	{
		var alts = new List<AltitudeMention>();

		foreach (Match match in FlightLevelRegex.Matches(text))
		{
			if (int.TryParse(match.Groups["fl"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var fl))
			{
				alts.Add(new AltitudeMention(fl * 100, $"FL{fl}"));
			}
		}

		foreach (Match match2 in AltitudeFtRegex.Matches(text))
		{
			if (int.TryParse(match2.Groups["alt"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var feet))
			{
				alts.Add(new AltitudeMention(feet, match2.Value.Trim()));
			}
		}

		foreach (Match match3 in AltitudeVerbRegex.Matches(text))
		{
			if (int.TryParse(match3.Groups["alt"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var feet))
			{
				alts.Add(new AltitudeMention(feet, match3.Groups["alt"].Value));
			}
		}

		return alts
			.Where(a => a.Feet >= 500 && a.Feet <= 50000)
			.GroupBy(a => a.Feet)
			.Select(g => g.First())
			.ToList();
	}

	private static List<FrequencyMention> ExtractFrequencies(string text)
	{
		return FrequencyRegex.Matches(text)
			.Select(m => m.Groups["freq"].Value)
			.Where(v => double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out _))
			.Select(v => new FrequencyMention(v))
			.GroupBy(f => f.ValueMhz)
			.Select(g => g.First())
			.ToList();
	}

	private static List<string> ExtractSids(string text)
	{
		var sids = new List<string>();

		void AddMatches(Regex regex)
		{
			foreach (Match match in regex.Matches(text))
			{
				var sid = NormalizeProcedure(match.Groups["sid"].Value);
				if (!string.IsNullOrWhiteSpace(sid))
					sids.Add(sid);
			}
		}

		AddMatches(SidRegex);
		AddMatches(ViaSidRegex);

		return sids.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
	}

	private static List<string> ExtractPotentialIcaos(string text)
	{
		return DestinationIcaoRegex.Matches(text)
			.Select(m => m.Groups["icao"].Value.ToUpperInvariant())
			.Where(t => t.Length == 4)
			.Distinct(StringComparer.OrdinalIgnoreCase)
			.ToList();
	}

	private static HashSet<string> GetAllowedRunways(AtcContext ctx, FlightContext flightCtx)
	{
		var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

		void Add(string? rw)
		{
			var norm = NormalizeRunway(rw);
			if (!string.IsNullOrWhiteSpace(norm))
				allowed.Add(norm);
		}

		Add(ctx?.ClearanceDecision?.DepRunway);
		Add(ctx?.ClearanceDecision?.ArrRunway);
		Add(flightCtx?.SelectedDepartureRunway);
		Add(flightCtx?.SelectedArrivalRunway);
		Add(flightCtx?.DepartureRunway?.RunwayIdentifier);
		Add(flightCtx?.ArrivalRunway?.RunwayIdentifier);

		return allowed;
	}

	private static HashSet<string> GetAllowedSquawks(AtcContext ctx, FlightContext flightCtx)
	{
		var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

		if (!string.IsNullOrWhiteSpace(ctx?.ClearanceDecision?.Squawk))
			allowed.Add(ctx.ClearanceDecision.Squawk);

		if (!string.IsNullOrWhiteSpace(flightCtx?.SquawkCode))
			allowed.Add(flightCtx.SquawkCode);

		return allowed;
	}

	private static HashSet<int> GetAllowedAltitudes(AtcContext ctx, FlightContext flightCtx)
	{
		var allowed = new HashSet<int>();

		void Add(int? feet)
		{
			if (feet.HasValue && feet.Value > 0)
				allowed.Add(feet.Value);
		}

		Add(ctx?.ClearanceDecision?.InitialAltitudeFt);
		Add(ctx?.ClearanceDecision?.ClearedAltitudeFt);
		Add(flightCtx?.ClearedAltitude);

		if (flightCtx != null && flightCtx.CruiseFlightLevel > 0)
			allowed.Add(flightCtx.CruiseFlightLevel * 100);

		var cruiseFromContext = ParseFlightLevel(ctx?.FlightInfo?.CruiseLevel);
		if (cruiseFromContext.HasValue)
			allowed.Add(cruiseFromContext.Value);

		return allowed;
	}

	private static HashSet<double> GetAllowedFrequencies(AtcContext ctx, FlightContext flightCtx)
	{
		var allowed = new HashSet<double>();

		if (ctx?.GroundFrequencyMhz.HasValue == true)
			allowed.Add(ctx.GroundFrequencyMhz.Value);

		if (!string.IsNullOrWhiteSpace(flightCtx?.CurrentFrequency) && double.TryParse(flightCtx.CurrentFrequency, NumberStyles.Float, CultureInfo.InvariantCulture, out var freq))
			allowed.Add(freq);

		return allowed;
	}

	private static HashSet<string> GetAllowedSids(AtcContext ctx, FlightContext flightCtx)
	{
		var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

		if (!string.IsNullOrWhiteSpace(ctx?.ClearanceDecision?.Sid))
			allowed.Add(NormalizeProcedure(ctx.ClearanceDecision.Sid));

		if (!string.IsNullOrWhiteSpace(flightCtx?.SelectedSID))
			allowed.Add(NormalizeProcedure(flightCtx.SelectedSID));

		return allowed;
	}

	private static string NormalizeRunway(string? value)
	{
		if (string.IsNullOrWhiteSpace(value))
			return string.Empty;

		var trimmed = value.Trim().ToUpperInvariant().Replace(" ", string.Empty);
		if (trimmed.StartsWith("RW", StringComparison.OrdinalIgnoreCase))
			trimmed = trimmed.Substring(2);

		if (trimmed.Length == 1)
			trimmed = "0" + trimmed;

		return trimmed;
	}

	private static string NormalizeProcedure(string value)
	{
		return value.Trim().ToUpperInvariant();
	}

	private static int? ParseFlightLevel(string? fl)
	{
		if (string.IsNullOrWhiteSpace(fl))
			return null;

		var cleaned = fl.ToUpperInvariant().Replace("FL", string.Empty).Replace(" ", string.Empty);
		if (int.TryParse(cleaned, NumberStyles.Integer, CultureInfo.InvariantCulture, out var flVal))
			return flVal * 100;

		return null;
	}

	private readonly record struct AltitudeMention(int Feet, string Raw);

	private readonly record struct FrequencyMention
	{
		public FrequencyMention(string raw)
		{
			Raw = raw;
			ValueMhz = double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) ? parsed : 0;
		}

		public string Raw { get; }
		public double ValueMhz { get; }
	}
}
