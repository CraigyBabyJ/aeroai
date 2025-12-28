using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace AeroAI.Atc;

/// <summary>
/// Post-processing guardrails to ensure ATC output never contains ICAO codes or raw callsigns.
/// Replaces them with spoken forms from ResolvedContext.
/// </summary>
public static class OutputGuard
{
	/// <summary>
	/// Scrubs the ATC response text, replacing ICAO codes and raw callsigns with spoken forms.
	/// </summary>
	/// <param name="text">The ATC response text to scrub.</param>
	/// <param name="context">Resolved context with spoken forms.</param>
	/// <param name="onDebug">Optional debug logging callback.</param>
	/// <returns>Scrubbed text with replacements applied.</returns>
	public static string ScrubOutput(string text, ResolvedContext? context, Action<string>? onDebug = null)
	{
		if (string.IsNullOrWhiteSpace(text) || context == null)
			return text ?? string.Empty;

		var result = text;

		// 1. Scrub airport ICAO codes
		if (context.HasDeparture)
		{
			result = ScrubAirportIcao(result, context.DepartureIcao!, context.DepartureSpoken!, "departure", onDebug);
		}

		if (context.HasArrival)
		{
			result = ScrubAirportIcao(result, context.ArrivalIcao!, context.ArrivalSpoken!, "arrival", onDebug);
		}

		// 2. Scrub raw callsign
		if (context.HasCallsign)
		{
			result = ScrubCallsign(result, context.CallsignRaw!, context.CallsignSpoken!, onDebug);
		}

		return result;
	}

	private static string ScrubAirportIcao(string text, string icao, string spoken, string airportType, Action<string>? onDebug)
	{
		if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(icao) || string.IsNullOrWhiteSpace(spoken))
			return text;

		var original = text;
		var upperIcao = icao.ToUpperInvariant();

		// Replace exact ICAO code matches (word boundaries to avoid partial matches)
		var pattern = $@"\b{Regex.Escape(upperIcao)}\b";
		var replaced = Regex.Replace(text, pattern, spoken, RegexOptions.IgnoreCase);

		if (replaced != original)
		{
			onDebug?.Invoke($"[OutputGuard] Replaced {airportType} ICAO '{upperIcao}' with '{spoken}'");
		}

		// Also try IATA code if it's a 3-letter code (common for major airports)
		// This is conservative - only replace if we're confident it's an airport code
		if (upperIcao.Length == 4 && replaced == original)
		{
			// Some airports have IATA codes that are the last 3 letters of ICAO
			// But we'll be conservative and only do this if explicitly needed
			// For now, skip IATA replacement to avoid false positives
		}

		return replaced;
	}

	private static string ScrubCallsign(string text, string callsignRaw, string callsignSpoken, Action<string>? onDebug)
	{
		if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(callsignRaw) || string.IsNullOrWhiteSpace(callsignSpoken))
			return text;

		var original = text;
		var upperRaw = callsignRaw.ToUpperInvariant();

		// Replace exact raw callsign matches (e.g., "ACA223")
		var pattern = $@"\b{Regex.Escape(upperRaw)}\b";
		var replaced = Regex.Replace(text, pattern, callsignSpoken, RegexOptions.IgnoreCase);

		if (replaced != original)
		{
			onDebug?.Invoke($"[OutputGuard] Replaced raw callsign '{upperRaw}' with '{callsignSpoken}'");
		}

		// Also replace common misheard variants if the raw callsign contains airline ICAO
		// This is conservative - only replace obvious misreadings
		if (replaced == original && upperRaw.Length >= 3)
		{
			// Extract airline ICAO (first 3 letters) and flight number
			var airlineIcao = upperRaw.Substring(0, Math.Min(3, upperRaw.Length));
			var flightNumber = upperRaw.Length > 3 ? upperRaw.Substring(3) : string.Empty;

			// Replace patterns like "ACA 223" (with space) or "ACA223" (without space)
			var variantPattern = $@"\b{Regex.Escape(airlineIcao)}\s*{Regex.Escape(flightNumber)}\b";
			replaced = Regex.Replace(text, variantPattern, callsignSpoken, RegexOptions.IgnoreCase);

			if (replaced != original)
			{
				onDebug?.Invoke($"[OutputGuard] Replaced callsign variant '{airlineIcao} {flightNumber}' with '{callsignSpoken}'");
			}
		}

		return replaced;
	}
}

