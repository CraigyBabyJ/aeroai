using System;
using System.Collections.Generic;

namespace AeroAI.Models;

public sealed class RunwaySelectionResult
{
	public NavRunwaySummary? SelectedRunway { get; init; }

	public IReadOnlyList<NavRunwaySummary> CandidatesConsidered { get; init; } = Array.Empty<NavRunwaySummary>();

	public string Reason { get; init; } = string.Empty;
}
