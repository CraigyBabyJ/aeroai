using System.Text.Json.Serialization;

namespace AeroAI.Atc;

public sealed class FlightInfo
{
	[JsonPropertyName("callsign")]
	public string Callsign { get; set; } = string.Empty;

	[JsonPropertyName("aircraft_type")]
	public string? AircraftType { get; set; }

	[JsonPropertyName("dep_icao")]
	public string? DepIcao { get; set; }

	[JsonPropertyName("dep_name")]
	public string? DepName { get; set; }

	[JsonPropertyName("arr_icao")]
	public string? ArrIcao { get; set; }

	[JsonPropertyName("arr_name")]
	public string? ArrName { get; set; }

	[JsonPropertyName("dep_airport_name")]
	public string? DepAirportName { get; set; }

	[JsonPropertyName("arr_airport_name")]
	public string? ArrAirportName { get; set; }

	[JsonPropertyName("cruise_level")]
	public string? CruiseLevel { get; set; }

	[JsonPropertyName("alternate_icao")]
	public string? AlternateIcao { get; set; }
}
