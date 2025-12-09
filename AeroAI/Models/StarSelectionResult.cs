namespace AeroAI.Models;

public sealed class StarSelectionResult
{
	public ProcedureSelectionMode Mode { get; init; }

	public StarSummary? SelectedStar { get; init; }

	public string? MatchingEntryFix { get; init; }

	public string Reason { get; init; } = string.Empty;
}
