using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace AeroAI.Atc;

/// <summary>
/// Lightweight validator to accept tolerant readbacks or request only missing items.
/// </summary>
public sealed class ReadbackValidationResult
{
	public bool Accepted { get; set; }
	public List<string> Missing { get; set; } = new();
	public List<string> Mismatched { get; set; } = new();
	public string? ResponseOverride { get; set; }
	public string? Callsign { get; set; }
}

public static class ReadbackValidator
{
	private const int CriticalThreshold = 2; // require 2/3 critical when available

	public static ReadbackValidationResult Evaluate(string pilotText, AtcContext atcContext, FlightContext flightContext, IReadOnlyCollection<string>? requiredSlots = null, string? issuedAtcText = null)
	{
		var result = new ReadbackValidationResult();

		if (atcContext?.ClearanceDecision == null)
			return result;

		var cd = atcContext.ClearanceDecision;
		var text = pilotText.ToUpperInvariant();

		var criticalPresent = 0;
		var criticalAvailable = 0;

		// Destination
		if (!string.IsNullOrWhiteSpace(cd.ClearedTo) && !ContainsToken(text, cd.ClearedTo))
			result.Missing.Add("destination");

		// SID
		if (!string.IsNullOrWhiteSpace(cd.Sid) && !ContainsToken(text, cd.Sid))
			result.Missing.Add("SID");

		// Runway (critical)
		if (!string.IsNullOrWhiteSpace(cd.DepRunway))
		{
			// Only validate runway if ATC actually issued it (prevents "advisory runway" from being treated as required readback).
			if (issuedAtcText == null || IssuedContainsRunway(issuedAtcText))
			{
				criticalAvailable++;
				if (ContainsToken(text, cd.DepRunway))
					criticalPresent++;
				else if (TryExtractRunway(text, out var rw) && !string.Equals(rw, cd.DepRunway, StringComparison.OrdinalIgnoreCase))
					result.Mismatched.Add("runway");
				else
					result.Missing.Add("runway");
			}
		}

		// Initial altitude (critical)
		if (cd.InitialAltitudeFt.HasValue)
		{
			criticalAvailable++;
			if (AltitudePresent(text, cd.InitialAltitudeFt.Value))
				criticalPresent++;
			else if (TryExtractAltitude(text, out var alt) && alt != cd.InitialAltitudeFt.Value)
				result.Mismatched.Add("initial altitude");
			else
				result.Missing.Add("initial altitude");
		}

		// Squawk (critical)
		if (!string.IsNullOrWhiteSpace(cd.Squawk))
		{
			criticalAvailable++;
			if (ContainsToken(text, cd.Squawk))
				criticalPresent++;
			else if (TryExtractSquawk(text, out var sq) && !string.Equals(sq, cd.Squawk, StringComparison.OrdinalIgnoreCase))
				result.Mismatched.Add("squawk");
			else
				result.Missing.Add("squawk");
		}

		// Expected FL (optional)
		var expectFl = flightContext?.CruiseFlightLevel;
		if (expectFl > 0 && !ContainsToken(text, $"FL{expectFl}"))
			result.Missing.Add("expect flight level");

		// If validating a specific subset, filter and accept based on that subset only.
		if (requiredSlots != null && requiredSlots.Count > 0)
		{
			var slotSet = new HashSet<string>(requiredSlots, StringComparer.OrdinalIgnoreCase);
			result.Missing = result.Missing.Where(slotSet.Contains).ToList();
			result.Mismatched = result.Mismatched.Where(slotSet.Contains).ToList();
			result.Accepted = result.Missing.Count == 0 && result.Mismatched.Count == 0;
		}
		else
		{
			// Acceptance policy (full validation)
			if (result.Mismatched.Count == 0 &&
			    criticalAvailable > 0 &&
			    criticalPresent >= CriticalThreshold)
			{
				result.Accepted = true;
			}
		}

		// Store callsign for downstream responses
		result.Callsign = !string.IsNullOrWhiteSpace(flightContext?.RadioCallsign)
			? flightContext.RadioCallsign
			: (flightContext?.Callsign ?? "Aircraft");

		return result;
	}

	private static bool ContainsToken(string text, string token)
	{
		if (string.IsNullOrWhiteSpace(token))
			return false;

		var normalizedText = Regex.Replace(text, @"\\s+", "");
		var normalizedToken = Regex.Replace(token, @"\\s+", "").ToUpperInvariant();
		return normalizedText.Contains(normalizedToken.ToUpperInvariant());
	}

	private static bool AltitudePresent(string text, int altitudeFt)
	{
		var normalized = text.Replace(",", "");

		// Accept raw feet (5000) or "FL050"/"FL50" style tokens for cruise levels.
		var feetToken = altitudeFt.ToString(CultureInfo.InvariantCulture);
		if (normalized.Contains(feetToken))
			return true;

		if (altitudeFt >= 10000)
		{
			var fl = altitudeFt / 100;
			if (normalized.Contains($"FL{fl}"))
				return true;
		}

		return false;
	}

	private static bool TryExtractRunway(string text, out string runway)
	{
		runway = string.Empty;
		var match = Regex.Match(text, "RUNWAY\\s*(\\d{2}[LRC]?)", RegexOptions.IgnoreCase);
		if (match.Success)
		{
			runway = match.Groups[1].Value.ToUpperInvariant();
			return true;
		}
		return false;
	}

	private static bool TryExtractAltitude(string text, out int altitudeFt)
	{
		altitudeFt = 0;
		var normalized = text.Replace(",", " ");
		var flMatch = Regex.Match(normalized, "FL\\s*(\\d{2,3})", RegexOptions.IgnoreCase);
		if (flMatch.Success && int.TryParse(flMatch.Groups[1].Value, out var fl))
		{
			altitudeFt = fl * 100;
			return true;
		}

		var ftMatch = Regex.Match(normalized, "(\\d{4,5})\\s*(FEET|FT)?", RegexOptions.IgnoreCase);
		if (ftMatch.Success && int.TryParse(ftMatch.Groups[1].Value, out var feet))
		{
			altitudeFt = feet;
			return true;
		}

		return false;
	}

	private static bool TryExtractSquawk(string text, out string code)
	{
		code = string.Empty;
		var match = Regex.Match(text, "\\b(\\d{4})\\b");
		if (match.Success)
		{
			code = match.Groups[1].Value;
			return true;
		}
		return false;
	}

	private static bool IssuedContainsRunway(string issuedAtcText)
	{
		if (string.IsNullOrWhiteSpace(issuedAtcText))
			return false;

		return Regex.IsMatch(issuedAtcText, "\\b(RUNWAY|RWY|DEPARTURE\\s+RUNWAY)\\b", RegexOptions.IgnoreCase);
	}
}
