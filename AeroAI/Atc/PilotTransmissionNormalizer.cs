using System;
using System.Text.RegularExpressions;
using AeroAI.Config;
using AeroAI.UI.Services;

namespace AeroAI.Atc;

/// <summary>
/// Centralized normalization pipeline for all pilot transmissions.
/// Applies STT corrections, callsign normalization, readback normalization, and deterministic fixes.
/// </summary>
public static class PilotTransmissionNormalizer
{
	private static ISttCorrectionLayer? _sttCorrectionLayer;
	private static readonly object _sync = new();

	/// <summary>
	/// Initialize the normalizer with STT correction layer (optional, can be null).
	/// </summary>
	public static void Initialize(ISttCorrectionLayer? sttCorrectionLayer = null)
	{
		lock (_sync)
		{
			_sttCorrectionLayer = sttCorrectionLayer;
		}
	}

	/// <summary>
	/// Normalize a pilot transmission through the complete pipeline:
	/// 1. STT corrections (if available)
	/// 2. Spoken number normalization (generic)
	/// 3. Deterministic fixes (AFR->IFR, EasyJet callsign fixes)
	/// 4. Callsign normalization
	/// 5. Readback normalization
	/// 6. Radio check typo fixes
	/// </summary>
	public static string Normalize(string rawText, FlightContext context, bool enableDebugLogging = false)
	{
		if (string.IsNullOrWhiteSpace(rawText))
			return rawText;

		var original = rawText;
		var current = rawText;

		// Step 1: Apply STT corrections (if available) - word confusions only
		lock (_sync)
		{
			if (_sttCorrectionLayer != null)
			{
				current = _sttCorrectionLayer.Apply(current);
			}
		}

		// Step 2: Spoken number normalization (generic - before callsign normalization)
		// Converts "ryanair one one three" -> "ryanair 113", "stand fifteen" -> "stand 15", etc.
		current = SpokenNumberNormalizer.Normalize(current);

		// Step 3: Deterministic STT corrections (AFR->IFR, EasyJet fixes)
		current = ApplyDeterministicCorrections(current);

		// Step 4: Callsign normalization
		current = CallsignNormalizer.Normalize(current, context);

		// Step 5: Readback normalization
		current = ReadbackNormalizer.Normalize(current, context);

		// Step 6: Radio check typo fixes
		current = FixRadioCheckTypos(current);

		if (enableDebugLogging && !string.Equals(original, current, StringComparison.Ordinal))
		{
			var logApi = Environment.GetEnvironmentVariable("AEROAI_LOG_API");
			if (!string.IsNullOrWhiteSpace(logApi) &&
			    (logApi.Equals("1", StringComparison.OrdinalIgnoreCase) ||
			     logApi.Equals("true", StringComparison.OrdinalIgnoreCase) ||
			     logApi.Equals("yes", StringComparison.OrdinalIgnoreCase)))
			{
				Console.WriteLine($"[NORMALIZE] Raw: \"{original}\" -> Normalized: \"{current}\"");
			}
		}

		return current;
	}

	/// <summary>
	/// Apply deterministic corrections that should always run:
	/// - AFR -> IFR (whole word, case-insensitive)
	/// - EasyJet callsign fixes (EZ/AZ/E Z/A Z -> EASY when followed by digits)
	/// </summary>
	private static string ApplyDeterministicCorrections(string text)
	{
		if (string.IsNullOrWhiteSpace(text))
			return text;

		var result = text;

		// AFR -> IFR (whole word, case-insensitive)
		result = Regex.Replace(result, @"\bAFR\b", "IFR", RegexOptions.IgnoreCase);

		// EasyJet callsign fixes: EZ/AZ/E Z/A Z followed by digits -> EASY
		// Pattern: \b(EZ|AZ|E Z|A Z)\s+(\d+) -> EASY $2
		result = Regex.Replace(result, @"\b(EZ|AZ|E\s+Z|A\s+Z)\s+(\d+)", "Easy $2", RegexOptions.IgnoreCase);

		// Also handle phonetic numbers after EZ/AZ
		var phoneticPattern = @"\b(EZ|AZ|E\s+Z|A\s+Z)\s+(one|two|three|four|five|six|seven|eight|nine|zero|oh|wun|too|tree|fower|fife|six|seven|ate|niner|zero|o)\s+(one|two|three|four|five|six|seven|eight|nine|zero|oh|wun|too|tree|fower|fife|six|seven|ate|niner|zero|o)\s+(one|two|three|four|five|six|seven|eight|nine|zero|oh|wun|too|tree|fower|fife|six|seven|ate|niner|zero|o)\b";
		result = Regex.Replace(result, phoneticPattern, "Easy $2 $3 $4", RegexOptions.IgnoreCase);

		return result;
	}

	/// <summary>
	/// Fix common radio check typos.
	/// </summary>
	private static string FixRadioCheckTypos(string text)
	{
		if (string.IsNullOrWhiteSpace(text))
			return text;

		var result = text;
		result = Regex.Replace(result, @"\bradio\s+chat\b", "radio check", RegexOptions.IgnoreCase);
		result = Regex.Replace(result, @"\bradio\s+chek\b", "radio check", RegexOptions.IgnoreCase);
		result = Regex.Replace(result, @"\bradio\s+ch[eai]t\b", "radio check", RegexOptions.IgnoreCase);
		return result;
	}
}

