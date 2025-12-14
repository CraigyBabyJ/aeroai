namespace AeroAI.Atc;

public class SimState
{
	public int AltitudeFeet { get; set; }

	public int GroundSpeedKts { get; set; }

	public bool OnGround { get; set; }

	public bool OnRunway { get; set; }

	public bool OnApproachCourse { get; set; }

	public bool OnFinal { get; set; }

	public int HeadingDegrees { get; set; }

	public double Latitude { get; set; }

	public double Longitude { get; set; }
}
