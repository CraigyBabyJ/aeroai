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
			return new CallsignDetails(raw, null, null, null, raw, null);
		}

		var airlineIcao = match.Groups["icao"].Value;
		var flightNumber = match.Groups["flight"].Value;
		var (radioName, fullName) = ResolveAirlineNames(airlineDirectory, airlineIcao);
		var radioCallsign = string.IsNullOrWhiteSpace(radioName)
			? $"{airlineIcao} {flightNumber}"
			: $"{radioName} {flightNumber}";

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
		if (directory.TryGetAirline(airlineIcao, out var airline))
		{
			var radio = airline.GetPreferredDisplay();
			var full = airline.Name;
			return (NormalizeAirlineName(radio), NormalizeAirlineName(full));
		}

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
