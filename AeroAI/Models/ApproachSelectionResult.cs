namespace AeroAI.Models;

public sealed class ApproachSelectionResult
{
	public ProcedureSelectionMode Mode { get; init; }

	public ApproachSummary? SelectedApproach { get; init; }

	public string Reason { get; init; } = string.Empty;
}
