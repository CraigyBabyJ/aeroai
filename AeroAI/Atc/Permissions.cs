using System.Text.Json.Serialization;

namespace AeroAI.Atc;

public sealed class Permissions
{
	[JsonPropertyName("allow_ifr_clearance")]
	public bool AllowIfrClearance { get; set; }

	[JsonPropertyName("allow_taxi")]
	public bool AllowTaxi { get; set; }

	[JsonPropertyName("allow_lineup")]
	public bool AllowLineup { get; set; }

	[JsonPropertyName("allow_takeoff_clearance")]
	public bool AllowTakeoffClearance { get; set; }

	[JsonPropertyName("allow_approach_clearance")]
	public bool AllowApproachClearance { get; set; }

	[JsonPropertyName("allow_landing_clearance")]
	public bool AllowLandingClearance { get; set; }
}
