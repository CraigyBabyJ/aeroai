using System.Text.Json.Serialization;

namespace AeroAI.Atc;

public sealed class CallsignContextInfo
{
	[JsonPropertyName("canonical")]
	public string? Canonical { get; set; }

	[JsonPropertyName("raw")]
	public string? Raw { get; set; }

	[JsonPropertyName("airline_icao")]
	public string? AirlineIcao { get; set; }

	[JsonPropertyName("flight_number")]
	public string? FlightNumber { get; set; }

	[JsonPropertyName("airline_radio_name")]
	public string? AirlineRadioName { get; set; }

	[JsonPropertyName("airline_full_name")]
	public string? AirlineFullName { get; set; }

	[JsonPropertyName("expected_variants")]
	public IReadOnlyList<string> ExpectedVariants { get; set; } = Array.Empty<string>();
}
