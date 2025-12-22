using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace AeroAI.Atc;

/// <summary>
/// Resolves aircraft type from natural speech to ICAO codes.
/// Accepts ICAO types directly, manufacturer + model, or spoken forms.
/// </summary>
public static class AircraftTypeResolver
{
	// Known aircraft families and their ICAO mappings
	private static readonly Dictionary<string, string> FamilyDefaults = new(StringComparer.OrdinalIgnoreCase)
	{
		// Boeing
		{ "737", "B738" },  // Default to -800 (most common)
		{ "747", "B744" },
		{ "757", "B752" },
		{ "767", "B763" },
		{ "777", "B77W" },
		{ "787", "B789" },
		
		// Airbus
		{ "318", "A318" },
		{ "319", "A319" },
		{ "320", "A320" },
		{ "321", "A321" },
		{ "330", "A333" },
		{ "340", "A343" },
		{ "350", "A359" },
		{ "380", "A388" },
	};

	// Variant suffixes and their ICAO code modifiers
	private static readonly Dictionary<string, Dictionary<string, string>> VariantMappings = new(StringComparer.OrdinalIgnoreCase)
	{
		{
			"737", new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
			{
				{ "700", "B737" },
				{ "seven hundred", "B737" },
				{ "800", "B738" },
				{ "eight hundred", "B738" },
				{ "900", "B739" },
				{ "nine hundred", "B739" },
				{ "max", "B38M" },
				{ "max 8", "B38M" },
				{ "max 9", "B39M" },
			}
		},
		{
			"320", new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
			{
				{ "neo", "A20N" },
				{ "ceo", "A320" },
			}
		},
		{
			"321", new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
			{
				{ "neo", "A21N" },
				{ "lr", "A21N" },
				{ "xlr", "A21N" },
			}
		},
		{
			"350", new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
			{
				{ "900", "A359" },
				{ "1000", "A35K" },
			}
		},
	};

	// STT typo corrections
	private static readonly Dictionary<string, string> TypoCorrections = new(StringComparer.OrdinalIgnoreCase)
	{
		{ "bowen", "boeing" },
		{ "bowing", "boeing" },
		{ "boweing", "boeing" },
		{ "airbus", "airbus" },  // Just for normalization
		{ "air bus", "airbus" },
		{ "arbus", "airbus" },
	};

	/// <summary>
	/// Attempt to resolve an aircraft type from natural speech or ICAO code.
	/// Returns (success, icaoCode, isAmbiguous).
	/// </summary>
	public static (bool Success, string? IcaoCode, bool IsAmbiguous) Resolve(string input)
	{
		if (string.IsNullOrWhiteSpace(input))
			return (false, null, false);

		var normalized = NormalizeInput(input);

        // 1. Direct ICAO code match (A320, B738, etc.)
        var icaoMatch = Regex.Match(normalized, @"\b([AB]\d{3}[A-Z]?|[AB]\d{2}[A-Z])\b", RegexOptions.IgnoreCase);
        if (icaoMatch.Success)
        {
                var icaoValue = icaoMatch.Value.ToUpperInvariant();
                var familyMatchFromIcao = Regex.Match(icaoValue, @"^[AB](\d{3})$");
                if (familyMatchFromIcao.Success)
                {
                        var familyFromIcao = familyMatchFromIcao.Groups[1].Value;
                        if (VariantMappings.TryGetValue(familyFromIcao, out var variants))
                        {
                                foreach (var (variantKey, icaoCode) in variants)
                                {
                                        if (normalized.Contains(variantKey, StringComparison.OrdinalIgnoreCase))
                                        {
                                                return (true, icaoCode, false);
                                        }
                                }
                        }
                }

                return (true, icaoValue, false);
        }

		// 2. Extract numeric family (737, 320, etc.)
		var familyMatch = Regex.Match(normalized, @"\b(7[0-9]{2}|3[0-9]{2})\b");
		if (familyMatch.Success)
		{
			var family = familyMatch.Value;
			
			// Check for variant specifier
			if (VariantMappings.TryGetValue(family, out var variants))
			{
				foreach (var (variantKey, icaoCode) in variants)
				{
					if (normalized.Contains(variantKey, StringComparison.OrdinalIgnoreCase))
					{
						return (true, icaoCode, false);
					}
				}
			}

			// No variant - use family default (ambiguous for 737)
			if (FamilyDefaults.TryGetValue(family, out var defaultIcao))
			{
				bool isAmbiguous = family == "737"; // 737 has many variants
				return (true, defaultIcao, isAmbiguous);
			}
		}

		// 3. Try manufacturer + model pattern
		var boeingMatch = Regex.Match(normalized, @"\bboeing\s*(\d{3})", RegexOptions.IgnoreCase);
		if (boeingMatch.Success)
		{
			var model = boeingMatch.Groups[1].Value;
			return ResolveFromFamily(model, normalized, "B");
		}

		var airbusMatch = Regex.Match(normalized, @"\bairbus\s*[aA]?(\d{3})", RegexOptions.IgnoreCase);
		if (airbusMatch.Success)
		{
			var model = airbusMatch.Groups[1].Value;
			return ResolveFromFamily(model, normalized, "A");
		}

		return (false, null, false);
	}

	private static (bool Success, string? IcaoCode, bool IsAmbiguous) ResolveFromFamily(string family, string fullInput, string prefix)
	{
		// Check for variant
		if (VariantMappings.TryGetValue(family, out var variants))
		{
			foreach (var (variantKey, icaoCode) in variants)
			{
				if (fullInput.Contains(variantKey, StringComparison.OrdinalIgnoreCase))
				{
					return (true, icaoCode, false);
				}
			}
		}

		// Use default
		if (FamilyDefaults.TryGetValue(family, out var defaultIcao))
		{
			bool isAmbiguous = family == "737";
			return (true, defaultIcao, isAmbiguous);
		}

		// Construct basic ICAO code
		return (true, $"{prefix}{family}", false);
	}

	/// <summary>
	/// Normalize input: fix typos, convert spoken numbers.
	/// </summary>
	private static string NormalizeInput(string input)
	{
		var result = input.Trim();

		// Fix STT typos
		foreach (var (typo, correction) in TypoCorrections)
		{
			result = Regex.Replace(result, $@"\b{Regex.Escape(typo)}\b", correction, RegexOptions.IgnoreCase);
		}

		// Convert spoken numbers using the generic normalizer
		result = ConvertSpokenAircraftNumbers(result);

		return result;
	}

	/// <summary>
	/// Convert spoken aircraft model numbers to digits.
	/// "seven three seven" -> "737"
	/// "three twenty" -> "320"
	/// </summary>
	private static string ConvertSpokenAircraftNumbers(string input)
	{
		var result = input;

		// Boeing patterns: "seven three seven", "seven eighty seven", "seven four seven"
		result = Regex.Replace(result, @"\bseven\s+three\s+seven\b", "737", RegexOptions.IgnoreCase);
		result = Regex.Replace(result, @"\bseven\s+four\s+seven\b", "747", RegexOptions.IgnoreCase);
		result = Regex.Replace(result, @"\bseven\s+five\s+seven\b", "757", RegexOptions.IgnoreCase);
		result = Regex.Replace(result, @"\bseven\s+six\s+seven\b", "767", RegexOptions.IgnoreCase);
		result = Regex.Replace(result, @"\bseven\s+seven\s+seven\b", "777", RegexOptions.IgnoreCase);
		result = Regex.Replace(result, @"\bseven\s+eighty\s+seven\b", "787", RegexOptions.IgnoreCase);
		result = Regex.Replace(result, @"\bseven\s+eight\s+seven\b", "787", RegexOptions.IgnoreCase);

		// Airbus patterns: "three twenty", "three nineteen", etc.
		result = Regex.Replace(result, @"\bthree\s+eighteen\b", "318", RegexOptions.IgnoreCase);
		result = Regex.Replace(result, @"\bthree\s+nineteen\b", "319", RegexOptions.IgnoreCase);
		result = Regex.Replace(result, @"\bthree\s+twenty\b", "320", RegexOptions.IgnoreCase);
		result = Regex.Replace(result, @"\bthree\s+two\s+zero\b", "320", RegexOptions.IgnoreCase);
		result = Regex.Replace(result, @"\bthree\s+twenty\s+one\b", "321", RegexOptions.IgnoreCase);
		result = Regex.Replace(result, @"\bthree\s+thirty\b", "330", RegexOptions.IgnoreCase);
		result = Regex.Replace(result, @"\bthree\s+three\s+zero\b", "330", RegexOptions.IgnoreCase);
		result = Regex.Replace(result, @"\bthree\s+forty\b", "340", RegexOptions.IgnoreCase);
		result = Regex.Replace(result, @"\bthree\s+fifty\b", "350", RegexOptions.IgnoreCase);
		result = Regex.Replace(result, @"\bthree\s+eighty\b", "380", RegexOptions.IgnoreCase);
		result = Regex.Replace(result, @"\bthree\s+eight\s+zero\b", "380", RegexOptions.IgnoreCase);

		// Variant patterns
		result = Regex.Replace(result, @"\beight\s+hundred\b", "800", RegexOptions.IgnoreCase);
		result = Regex.Replace(result, @"\bseven\s+hundred\b", "700", RegexOptions.IgnoreCase);
		result = Regex.Replace(result, @"\bnine\s+hundred\b", "900", RegexOptions.IgnoreCase);

		return result;
	}

	/// <summary>
	/// Simple resolve that returns just the ICAO code or null (for backward compatibility).
	/// </summary>
	public static string? ResolveSimple(string input)
	{
		var (success, icaoCode, _) = Resolve(input);
		return success ? icaoCode : null;
	}

	/// <summary>
	/// Check if the input contains any aircraft type mention.
	/// </summary>
	public static bool ContainsAircraftType(string input)
	{
		if (string.IsNullOrWhiteSpace(input))
			return false;

		var normalized = NormalizeInput(input);

		// Check for ICAO codes
		if (Regex.IsMatch(normalized, @"\b[AB]\d{3}[A-Z]?\b", RegexOptions.IgnoreCase))
			return true;

		// Check for manufacturer mentions
		if (Regex.IsMatch(normalized, @"\b(boeing|airbus)\b", RegexOptions.IgnoreCase))
			return true;

		// Check for numeric family
		if (Regex.IsMatch(normalized, @"\b(7[0-9]{2}|3[0-9]{2})\b"))
			return true;

		return false;
	}
}
