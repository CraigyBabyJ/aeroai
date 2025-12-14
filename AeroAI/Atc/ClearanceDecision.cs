using System.Text.Json.Serialization;

namespace AeroAI.Atc;

public sealed class ClearanceDecision
{
	[JsonPropertyName("clearance_type")]
	public string ClearanceType { get; set; } = string.Empty;

	[JsonPropertyName("cleared_to")]
	public string? ClearedTo { get; set; }

	[JsonPropertyName("route_summary")]
	public string? RouteSummary { get; set; }

	[JsonPropertyName("dep_runway")]
	public string? DepRunway { get; set; }

	[JsonPropertyName("arr_runway")]
	public string? ArrRunway { get; set; }

	[JsonPropertyName("sid")]
	public string? Sid { get; set; }

	[JsonPropertyName("star")]
	public string? Star { get; set; }

	[JsonPropertyName("approach")]
	public string? Approach { get; set; }

	[JsonPropertyName("via_radar_vectors")]
	public bool ViaRadarVectors { get; set; }

	[JsonPropertyName("initial_altitude_ft")]
	public int? InitialAltitudeFt { get; set; }

	[JsonPropertyName("cleared_altitude_ft")]
	public int? ClearedAltitudeFt { get; set; }

	[JsonPropertyName("cleared_heading_deg")]
	public int? ClearedHeadingDeg { get; set; }

	[JsonPropertyName("speed_restriction_kt")]
	public int? SpeedRestrictionKt { get; set; }

	[JsonPropertyName("squawk")]
	public string? Squawk { get; set; }

	[JsonPropertyName("callsign_info")]
	public CallsignContextInfo? CallsignInfo { get; set; }
}
