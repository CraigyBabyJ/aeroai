namespace AeroAI.Models;

public sealed class SidSelectionResult
{
	public ProcedureSelectionMode Mode { get; init; }

	public SidSummary? SelectedSid { get; init; }

	public string? MatchingExitFix { get; init; }

	public string Reason { get; init; } = string.Empty;
}
