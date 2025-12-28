using System;
using System.Text.RegularExpressions;
using AeroAI.Data;

namespace AeroAI.Atc;

/// <summary>
/// Builds ResolvedContext from FlightContext and SimBrief data, ensuring authoritative
/// data is used and spoken forms are generated for TTS.
/// </summary>
public static class ResolvedContextBuilder
{
	/// <summary>
	/// Build resolved context from FlightContext. Uses SimBrief data as primary source,
	/// falls back to airports.json for airport names if SimBrief lacks city/name.
	/// </summary>
	public static ResolvedContext Build(FlightContext? flightContext)
	{
		if (flightContext == null)
			return new ResolvedContext();

		// Build callsign resolution
		var callsignRaw = GetCallsignRaw(flightContext);
		var callsignSpoken = GetCallsignSpoken(flightContext);

		// Build departure airport resolution
		var (depSpoken, depSource) = ResolveAirportName(
			flightContext.OriginIcao,
			flightContext.OriginName,
			flightContext);

		// Build arrival airport resolution
		var (arrSpoken, arrSource) = ResolveAirportName(
			flightContext.DestinationIcao,
			flightContext.DestinationName,
			flightContext);

		return new ResolvedContext
		{
			CallsignRaw = callsignRaw,
			CallsignSpoken = callsignSpoken,
			DepartureIcao = string.IsNullOrWhiteSpace(flightContext.OriginIcao) ? null : flightContext.OriginIcao.Trim().ToUpperInvariant(),
			ArrivalIcao = string.IsNullOrWhiteSpace(flightContext.DestinationIcao) ? null : flightContext.DestinationIcao.Trim().ToUpperInvariant(),
			DepartureSpoken = depSpoken,
			ArrivalSpoken = arrSpoken,
			DepartureSource = depSource,
			ArrivalSource = arrSource
		};
	}

	private static string? GetCallsignRaw(FlightContext context)
	{
		// Prefer SimBrief raw callsign (e.g., "ACA223")
		if (!string.IsNullOrWhiteSpace(context.RawCallsign))
			return context.RawCallsign.Trim().ToUpperInvariant();

		// Fallback: construct from airline ICAO + flight number
		if (!string.IsNullOrWhiteSpace(context.AirlineIcao) && !string.IsNullOrWhiteSpace(context.FlightNumber))
			return $"{context.AirlineIcao.Trim().ToUpperInvariant()}{context.FlightNumber.Trim()}";

		return null;
	}

	private static string? GetCallsignSpoken(FlightContext context)
	{
		// Prefer RadioCallsign which should already be in spoken form (e.g., "Air Canada 223")
		if (!string.IsNullOrWhiteSpace(context.RadioCallsign))
		{
			// Convert numbers to digit-by-digit format for TTS
			return ConvertCallsignNumbersToSpoken(context.RadioCallsign);
		}

		// Fallback: construct from airline name + flight number
		if (!string.IsNullOrWhiteSpace(context.AirlineName) && !string.IsNullOrWhiteSpace(context.FlightNumber))
		{
			var numberSpoken = ConvertNumberToSpoken(context.FlightNumber);
			return $"{context.AirlineName.Trim()} {numberSpoken}";
		}

		// Last resort: use Callsign if available
		if (!string.IsNullOrWhiteSpace(context.Callsign))
			return ConvertCallsignNumbersToSpoken(context.Callsign);

		return null;
	}

	/// <summary>
	/// Converts flight numbers in callsigns to digit-by-digit format.
	/// "Air Canada 223" -> "Air Canada two two three"
	/// </summary>
	private static string ConvertCallsignNumbersToSpoken(string callsign)
	{
		// Pattern: airline name followed by space and digits
		return Regex.Replace(callsign, @"\s+(\d{1,4})\b", match =>
		{
			var number = match.Groups[1].Value;
			var spoken = ConvertNumberToSpoken(number);
			return $" {spoken}";
		});
	}

	/// <summary>
	/// Converts a number string to digit-by-digit spoken form.
	/// "223" -> "two two three"
	/// </summary>
	private static string ConvertNumberToSpoken(string number)
	{
		if (string.IsNullOrWhiteSpace(number))
			return number;

		var words = new System.Collections.Generic.List<string>();
		foreach (var digit in number)
		{
			words.Add(digit switch
			{
				'0' => "zero",
				'1' => "one",
				'2' => "two",
				'3' => "three",
				'4' => "four",
				'5' => "five",
				'6' => "six",
				'7' => "seven",
				'8' => "eight",
				'9' => "nine",
				_ => digit.ToString()
			});
		}
		return string.Join(" ", words);
	}

	/// <summary>
	/// Resolves airport name with source tracking. Prefers SimBrief name, falls back to airports.json.
	/// </summary>
	private static (string? spoken, string source) ResolveAirportName(
		string? icao,
		string? simbriefName,
		FlightContext? flightContext)
	{
		if (string.IsNullOrWhiteSpace(icao))
			return (null, "none");

		var upperIcao = icao.Trim().ToUpperInvariant();

		// Prefer SimBrief name if available
		if (!string.IsNullOrWhiteSpace(simbriefName))
		{
			// Prefer city name (usually shorter, more natural)
			// SimBrief names are often full airport names, try to extract city
			var cityName = ExtractCityFromAirportName(simbriefName);
			return (cityName ?? simbriefName.Trim(), "simbrief");
		}

		// Fallback to airports.json lookup
		var resolved = AirportNameResolver.ResolveAirportName(upperIcao, flightContext);
		if (!string.IsNullOrWhiteSpace(resolved) && !resolved.Equals(upperIcao, StringComparison.OrdinalIgnoreCase))
		{
			// Prefer city name from airports.json
			var cityName = ExtractCityFromAirportName(resolved);
			return (cityName ?? resolved.Trim(), "airports.json");
		}

		// Last resort: return ICAO (should not happen in practice, but guard against it)
		return (upperIcao, "icao_fallback");
	}

	/// <summary>
	/// Attempts to extract city name from full airport name.
	/// "Calgary International Airport" -> "Calgary"
	/// "Vancouver International" -> "Vancouver"
	/// </summary>
	private static string? ExtractCityFromAirportName(string fullName)
	{
		if (string.IsNullOrWhiteSpace(fullName))
			return null;

		var trimmed = fullName.Trim();

		// Common patterns: "City International Airport", "City International", "City Airport"
		var patterns = new[]
		{
			@"^(.+?)\s+International\s+Airport",
			@"^(.+?)\s+International",
			@"^(.+?)\s+Airport"
		};

		foreach (var pattern in patterns)
		{
			var match = Regex.Match(trimmed, pattern, RegexOptions.IgnoreCase);
			if (match.Success && match.Groups.Count > 1)
			{
				var city = match.Groups[1].Value.Trim();
				if (!string.IsNullOrWhiteSpace(city) && city.Length > 2)
					return city;
			}
		}

		// If no pattern matches, return the full name
		return trimmed;
	}
}

