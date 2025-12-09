namespace AeroAI.Atc;

public sealed class ArrivalVectoringState
{
	public ArrivalVectorPhase Phase { get; set; } = ArrivalVectorPhase.Positioning;

	public double? InterceptLatitude { get; set; }

	public double? InterceptLongitude { get; set; }

	public int? FinalApproachCourse { get; set; }

	public bool ClearedForApproach { get; set; }
}
