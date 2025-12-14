namespace AeroAI.Models;

public sealed class NavRunwaySummary
{
	public string AirportIcao { get; init; } = string.Empty;

	public string RunwayIdentifier { get; init; } = string.Empty;

	public int TrueHeadingDegrees { get; init; }

	public int LengthFeet { get; init; }

	public bool HasIlsOrLocalizer { get; init; }

	public bool HasRnavApproach { get; init; }

	public bool IsPreferredDeparture { get; init; }

	public bool IsPreferredArrival { get; init; }
}
