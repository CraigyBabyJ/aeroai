namespace AeroAI.Models;

public sealed class AircraftPerformanceProfile
{
	public string IcaoType { get; init; } = string.Empty;

	public int RequiredTakeoffDistanceFeet { get; init; }

	public int RequiredLandingDistanceFeet { get; init; }

	public int MaxTailwindComponentKnots { get; init; } = 10;

	public int MaxCrosswindComponentKnots { get; init; } = 25;
}
