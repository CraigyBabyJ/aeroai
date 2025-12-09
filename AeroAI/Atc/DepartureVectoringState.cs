namespace AeroAI.Atc;

public sealed class DepartureVectoringState
{
	public bool HasLeftRunwayHeading { get; set; }

	public bool HasResumedOwnNavigation { get; set; }

	public int? TargetHeading { get; set; }
}
