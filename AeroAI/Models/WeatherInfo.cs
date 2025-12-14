namespace AeroAI.Models;

public sealed class WeatherInfo
{
	public string AirportIcao { get; init; } = string.Empty;

	public int WindDirectionDegrees { get; init; }

	public int WindSpeedKnots { get; init; }

	public int VisibilityMeters { get; init; }

	public int CeilingFeet { get; init; }

	public bool IsIfr { get; init; }

	public bool IsLowVisibility { get; init; }
}
