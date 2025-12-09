namespace AeroAI.Models;

public sealed class SimState
{
	public double Latitude { get; init; }

	public double Longitude { get; init; }

	public int AltitudeFeet { get; init; }

	public int HeadingDegrees { get; init; }

	public int GroundSpeedKnots { get; init; }

	public bool OnGround { get; init; }
}
