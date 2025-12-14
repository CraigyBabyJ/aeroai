using System.Text.Json;
using System.Text.Json.Serialization;

namespace AeroAI.Atc;

public sealed class AtcContext
{
	[JsonPropertyName("controller_role")]
	public string ControllerRole { get; set; } = string.Empty;

	[JsonPropertyName("phase")]
	public string Phase { get; set; } = string.Empty;

	[JsonPropertyName("flight_info")]
	public FlightInfo FlightInfo { get; set; } = new FlightInfo();

	[JsonPropertyName("clearance_decision")]
	public ClearanceDecision ClearanceDecision { get; set; } = new ClearanceDecision();

	[JsonPropertyName("weather_relevant")]
	public WeatherRelevant WeatherRelevant { get; set; } = new WeatherRelevant();

	[JsonPropertyName("state_flags")]
	public StateFlags StateFlags { get; set; } = new StateFlags();

	[JsonPropertyName("permissions")]
	public Permissions Permissions { get; set; } = new Permissions();

	[JsonPropertyName("callsign_info")]
	public CallsignContextInfo? CallsignInfo { get; set; }

	[JsonPropertyName("ground_frequency_mhz")]
	public double? GroundFrequencyMhz { get; set; }

	public string ToJson()
	{
		JsonSerializerOptions options = new JsonSerializerOptions
		{
			WriteIndented = true,
			DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
		};
		return JsonSerializer.Serialize(this, options);
	}
}
