using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace AeroAI.Atc;

/// <summary>
/// Converts spoken numbers into digits in common ATC contexts.
/// Conservative: only converts when near known keywords or patterns.
/// </summary>
public static class SpokenNumberNormalizer
{
	// Spoken number mappings
	private static readonly Dictionary<string, string> SpokenDigits = new(StringComparer.OrdinalIgnoreCase)
	{
		// Single digits
		{ "zero", "0" }, { "oh", "0" },
		{ "one", "1" }, { "wun", "1" },
		{ "two", "2" }, { "too", "2" },
		{ "three", "3" }, { "tree", "3" },
		{ "four", "4" }, { "fower", "4" },
		{ "five", "5" }, { "fife", "5" },
		{ "six", "6" },
		{ "seven", "7" },
		{ "eight", "8" }, { "ait", "8" },
		{ "nine", "9" }, { "niner", "9" },
		
		// Teens
		{ "ten", "10" },
		{ "eleven", "11" },
		{ "twelve", "12" },
		{ "thirteen", "13" },
		{ "fourteen", "14" },
		{ "fifteen", "15" },
		{ "sixteen", "16" },
		{ "seventeen", "17" },
		{ "eighteen", "18" },
		{ "nineteen", "19" },
		
		// Tens
		{ "twenty", "20" },
		{ "thirty", "30" },
		{ "forty", "40" },
		{ "fifty", "50" },
		{ "sixty", "60" },
		{ "seventy", "70" },
		{ "eighty", "80" },
		{ "ninety", "90" },
		
		// Hundreds for altitudes/flight levels
		{ "hundred", "00" },
		{ "thousand", "000" },
	};

	// Common airline names that precede callsign numbers
	private static readonly HashSet<string> AirlineKeywords = new(StringComparer.OrdinalIgnoreCase)
	{
		"ryanair", "easyjet", "easy", "speedbird", "british", "lufthansa", "air france",
		"klm", "iberia", "vueling", "wizz", "wizzair", "jet2", "tui", "aer lingus",
		"virgin", "united", "delta", "american", "southwest", "jetblue",
		"emirates", "qatar", "etihad", "turkish", "swiss", "austrian",
		"scandinavian", "norwegian", "finnair", "icelandair", "tap",
		"shamrock", "springbok", "qantas", "singapore", "cathay",
	};

	/// <summary>
	/// Normalize spoken numbers in the entire transmission based on context.
	/// </summary>
	public static string Normalize(string text)
	{
		if (string.IsNullOrWhiteSpace(text))
			return text;

		var result = text;

		// 1. Normalize callsign numbers (after airline name or at end)
		result = NormalizeCallsignNumbers(result);

		// 2. Normalize stand/gate numbers
		result = NormalizeStandGateNumbers(result);

		// 3. Normalize squawk codes
		result = NormalizeSquawkNumbers(result);

		// 4. Normalize flight level / altitude numbers
		result = NormalizeFlightLevelNumbers(result);

		return result;
	}

	/// <summary>
	/// Convert spoken digits following an airline name to numeric form.
	/// "ryanair one one three" -> "ryanair 113"
	/// </summary>
	private static string NormalizeCallsignNumbers(string text)
	{
		var result = text;

		// Build pattern to match airline followed by spoken numbers
		foreach (var airline in AirlineKeywords)
		{
			// Match airline followed by sequence of spoken digits
			var pattern = $@"\b{Regex.Escape(airline)}\s+((?:(?:{GetSpokenNumberPattern()})\s*)+)";
			result = Regex.Replace(result, pattern, match =>
			{
				var airlinePart = airline;
				var numberPart = match.Groups[1].Value;
				var digits = ConvertSpokenSequenceToDigits(numberPart);
				return $"{airlinePart} {digits}";
			}, RegexOptions.IgnoreCase);
		}

		// Also handle end-of-transmission pattern: spoken digits at the end
		// This catches callsigns without explicit airline prefix that end with numbers
		// e.g., "requesting clearance one one three" -> "requesting clearance 113"
		var endPattern = @"\b((?:(?:" + GetSpokenNumberPattern() + @")\s+){2,})$";
		result = Regex.Replace(result, endPattern, match =>
		{
			var digits = ConvertSpokenSequenceToDigits(match.Groups[1].Value);
			return digits;
		}, RegexOptions.IgnoreCase);

		return result;
	}

	/// <summary>
	/// Normalize numbers after stand/gate keywords.
	/// "stand fifteen" -> "stand 15"
	/// "gate bravo twelve" -> "gate bravo 12"
	/// </summary>
	private static string NormalizeStandGateNumbers(string text)
	{
		var pattern = @"\b(stand|gate|parking|bay|position|ramp)\s+((?:(?:" + GetSpokenNumberPattern() + @"|[a-z]+)\s*)+)";
		return Regex.Replace(text, pattern, match =>
		{
			var keyword = match.Groups[1].Value;
			var rest = match.Groups[2].Value.Trim();
			var normalized = ConvertSpokenSequenceToDigits(rest);
			return $"{keyword} {normalized}";
		}, RegexOptions.IgnoreCase);
	}

	/// <summary>
	/// Normalize squawk codes.
	/// "squawk five two four zero" -> "squawk 5240"
	/// </summary>
	private static string NormalizeSquawkNumbers(string text)
	{
		var pattern = @"\b(squawk)\s+((?:(?:" + GetSpokenNumberPattern() + @")\s*)+)";
		return Regex.Replace(text, pattern, match =>
		{
			var keyword = match.Groups[1].Value;
			var rest = match.Groups[2].Value.Trim();
			var digits = ConvertSpokenSequenceToDigits(rest);
			return $"{keyword} {digits}";
		}, RegexOptions.IgnoreCase);
	}

	/// <summary>
	/// Normalize flight level and altitude numbers.
	/// "flight level three three zero" -> "flight level 330"
	/// </summary>
	private static string NormalizeFlightLevelNumbers(string text)
	{
		// Flight level pattern
		var flPattern = @"\b(flight\s+level|FL)\s+((?:(?:" + GetSpokenNumberPattern() + @")\s*)+)";
		var result = Regex.Replace(text, flPattern, match =>
		{
			var keyword = match.Groups[1].Value;
			var rest = match.Groups[2].Value.Trim();
			var digits = ConvertSpokenSequenceToDigits(rest);
			return $"{keyword} {digits}";
		}, RegexOptions.IgnoreCase);

		// Altitude with "feet" or "thousand"
		var altPattern = @"\b(altitude|climb|descend(?:\s+to)?|maintain)\s+((?:(?:" + GetSpokenNumberPattern() + @")\s*)+)\s*(feet|foot)?";
		result = Regex.Replace(result, altPattern, match =>
		{
			var keyword = match.Groups[1].Value;
			var rest = match.Groups[2].Value.Trim();
			var suffix = match.Groups[3].Value;
			var digits = ConvertSpokenSequenceToDigits(rest);
			return string.IsNullOrWhiteSpace(suffix) ? $"{keyword} {digits}" : $"{keyword} {digits} {suffix}";
		}, RegexOptions.IgnoreCase);

		return result;
	}

	/// <summary>
	/// Build regex pattern matching any spoken number word.
	/// </summary>
	private static string GetSpokenNumberPattern()
	{
		var words = new List<string>(SpokenDigits.Keys);
		return string.Join("|", words.ConvertAll(w => Regex.Escape(w)));
	}

	/// <summary>
	/// Convert a sequence of spoken number words to digit string.
	/// "one one three" -> "113"
	/// "fifteen" -> "15"
	/// "three three zero" -> "330"
	/// </summary>
	public static string ConvertSpokenSequenceToDigits(string spokenSequence)
	{
		if (string.IsNullOrWhiteSpace(spokenSequence))
			return spokenSequence;

		var words = spokenSequence.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
		var result = new List<string>();

		foreach (var word in words)
		{
			var cleanWord = word.Trim();
			if (SpokenDigits.TryGetValue(cleanWord, out var digit))
			{
				result.Add(digit);
			}
			else if (Regex.IsMatch(cleanWord, @"^\d+$"))
			{
				// Already a number
				result.Add(cleanWord);
			}
			else
			{
				// Not a number word - keep as-is (e.g., phonetic letters like "bravo")
				result.Add(cleanWord);
			}
		}

		// Join without spaces for pure digit sequences, with spaces for mixed
		bool allDigits = result.TrueForAll(s => Regex.IsMatch(s, @"^\d+$"));
		return allDigits ? string.Concat(result) : string.Join(" ", result);
	}

	/// <summary>
	/// Convert a single spoken number word to its digit equivalent.
	/// </summary>
	public static string? ConvertSingleWord(string word)
	{
		if (string.IsNullOrWhiteSpace(word))
			return null;
		return SpokenDigits.TryGetValue(word.Trim(), out var digit) ? digit : null;
	}
}

