namespace AeroAI.Models;

public sealed class WeatherInfo
{
	public string AirportIcao { get; init; } = string.Empty;

	/// <summary>
	/// Raw METAR string if available (best-effort).
	/// </summary>
	public string? RawMetar { get; init; }

	public int WindDirectionDegrees { get; init; }

	public int WindSpeedKnots { get; init; }

	public int VisibilityMeters { get; init; }

	public int CeilingFeet { get; init; }

	public bool IsIfr { get; init; }

	public bool IsLowVisibility { get; init; }
}
