using System.Text.Json.Serialization;

namespace AeroAI.Atc;

public sealed class StateFlags
{
	[JsonPropertyName("ifr_clearance_issued")]
	public bool IfrClearanceIssued { get; set; }

	[JsonPropertyName("taxi_clearance_issued")]
	public bool TaxiClearanceIssued { get; set; }

	[JsonPropertyName("lineup_issued")]
	public bool LineupIssued { get; set; }

	[JsonPropertyName("takeoff_issued")]
	public bool TakeoffIssued { get; set; }

	[JsonPropertyName("approach_issued")]
	public bool ApproachIssued { get; set; }

	[JsonPropertyName("landing_issued")]
	public bool LandingIssued { get; set; }
}
