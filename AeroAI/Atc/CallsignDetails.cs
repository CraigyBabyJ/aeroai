using System.Globalization;
using System.Text.RegularExpressions;
using AeroAI.Data;

namespace AeroAI.Atc;

public sealed class CallsignDetails
{
	private static readonly Regex AirlineCallsignPattern = new("^(?<icao>[A-Z]{3})(?<flight>\\d{1,4})$", RegexOptions.Compiled);

	public CallsignDetails(string raw, string? airlineIcao, string? flightNumber, string? airlineRadioName, string? radioCallsign, string? airlineFullName, string? canonicalCallsign = null)
	{
		Raw = raw;
		AirlineIcao = airlineIcao;
		FlightNumber = flightNumber;
		AirlineName = airlineRadioName;
		AirlineFullName = airlineFullName;
		RadioCallsign = !string.IsNullOrWhiteSpace(radioCallsign) ? radioCallsign : raw;
		CanonicalCallsign = canonicalCallsign ?? raw;
	}

	public string Raw { get; }

	public string? AirlineIcao { get; }

	public string? FlightNumber { get; }

	/// <summary>
	/// Airline radio callsign (e.g., SCANDINAVIAN, SPEEDBIRD).
	/// </summary>
	public string? AirlineName { get; }

	/// <summary>
	/// Airline marketing name (e.g., Cathay Pacific, Air New Zealand).
	/// </summary>
	public string? AirlineFullName { get; }

	public string RadioCallsign { get; }

	public string CanonicalCallsign { get; }

	public bool IsValid => !string.IsNullOrWhiteSpace(AirlineIcao) && !string.IsNullOrWhiteSpace(FlightNumber);

	public static CallsignDetails FromRaw(string? rawCallsign, AirlineDirectory airlineDirectory)
	{
		var raw = (rawCallsign ?? string.Empty).Trim().ToUpperInvariant();
		if (string.IsNullOrWhiteSpace(raw))
		{
			return new CallsignDetails(string.Empty, null, null, null, string.Empty, null);
		}

		var match = AirlineCallsignPattern.Match(raw);
		if (!match.Success)
		{
			System.Diagnostics.Debug.WriteLine($"[CALLSIGN] Pattern match failed for '{raw}'");
			return new CallsignDetails(raw, null, null, null, raw, null);
		}

		var airlineIcao = match.Groups["icao"].Value;
		var flightNumber = match.Groups["flight"].Value;
		System.Diagnostics.Debug.WriteLine($"[CALLSIGN] Parsed '{raw}' -> ICAO={airlineIcao}, Flight={flightNumber}");
		
		var (radioName, fullName) = ResolveAirlineNames(airlineDirectory, airlineIcao);
		// Radio callsigns: use the airline name (e.g., "Air Canada") not the ICAO code (e.g., "ACA")
		// If we have a resolved airline name, use it; otherwise fall back to ICAO code
		var radioCallsign = string.IsNullOrWhiteSpace(radioName)
			? $"{airlineIcao} {flightNumber}"
			: $"{radioName} {flightNumber}";

		System.Diagnostics.Debug.WriteLine($"[CALLSIGN] Final: RadioCallsign='{radioCallsign}', AirlineName='{radioName}', AirlineFullName='{fullName}'");

		return new CallsignDetails(raw, airlineIcao, flightNumber, radioName, radioCallsign, fullName, raw);
	}

	public static CallsignDetails FromContext(FlightContext context)
	{
		var canonical = string.IsNullOrWhiteSpace(context.AirlineIcao) || string.IsNullOrWhiteSpace(context.FlightNumber)
			? context.CanonicalCallsign ?? context.Callsign
			: $"{context.AirlineIcao}{context.FlightNumber}";

		var radioCallsign = !string.IsNullOrWhiteSpace(context.Callsign)
			? context.Callsign
			: canonical ?? context.RawCallsign;

		return new CallsignDetails(
			context.RawCallsign,
			context.AirlineIcao,
			context.FlightNumber,
			context.AirlineName,
			radioCallsign,
			context.AirlineFullName,
			context.CanonicalCallsign ?? canonical);
	}

	private static (string? radioName, string? fullName) ResolveAirlineNames(AirlineDirectory directory, string airlineIcao)
	{
		if (!directory.HasData)
		{
			System.Diagnostics.Debug.WriteLine($"[CALLSIGN] AirlineDirectory has no data (source: {directory.SourcePath})");
			return (null, null);
		}

		if (directory.TryGetAirline(airlineIcao, out var airline))
		{
			// Use the airline name (e.g., "Air Canada") for radio callsign, not the call_sign (e.g., "AIR CANADA")
			// The call_sign is the radio phraseology, but we want the readable name for display
			var radio = airline.Name; // Use name field, not call_sign
			var full = airline.Name;
			var normalizedRadio = NormalizeAirlineName(radio);
			var normalizedFull = NormalizeAirlineName(full);
			System.Diagnostics.Debug.WriteLine($"[CALLSIGN] Resolved {airlineIcao}: name='{airline.Name}', call_sign='{airline.CallSign}', using name='{normalizedRadio}'");
			return (normalizedRadio, normalizedFull);
		}

		System.Diagnostics.Debug.WriteLine($"[CALLSIGN] Airline {airlineIcao} not found in directory (has {directory.SourcePath})");
		return (null, null);
	}

	private static string? NormalizeAirlineName(string? value)
	{
		if (string.IsNullOrWhiteSpace(value))
		{
			return null;
		}

		var lower = value.ToLowerInvariant();
		return CultureInfo.InvariantCulture.TextInfo.ToTitleCase(lower);
	}
}
