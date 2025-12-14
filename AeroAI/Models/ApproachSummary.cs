namespace AeroAI.Models;

public sealed class ApproachSummary
{
	public string AirportIcao { get; init; } = string.Empty;

	public string ProcedureIdentifier { get; init; } = string.Empty;

	public string RouteType { get; init; } = string.Empty;

	public string RunwayIdentifier { get; init; } = string.Empty;

	public string ApproachTypeCode { get; init; } = string.Empty;

	public bool HasGlideslope { get; init; }

	public bool IsRnav { get; init; }

	public bool IsCirclingOnly { get; init; }

	public bool SupportsStraightIn { get; init; }

	public string InitialApproachFixIdentifier { get; init; } = string.Empty;

	public string FinalApproachFixIdentifier { get; init; } = string.Empty;
}
