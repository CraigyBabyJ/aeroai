namespace AeroAI.Models;

public sealed class StarSummary
{
	public string AirportIcao { get; init; } = string.Empty;

	public string ProcedureIdentifier { get; init; } = string.Empty;

	public string RouteType { get; init; } = string.Empty;

	public string RunwayIdentifier { get; init; } = string.Empty;

	public string TransitionIdentifier { get; init; } = string.Empty;

	public string EntryFixIdentifier { get; init; } = string.Empty;

	public string ExitFixIdentifier { get; init; } = string.Empty;
}
