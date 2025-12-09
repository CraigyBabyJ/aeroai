using System.Collections.Generic;

namespace AeroAI.Atc;

public class PilotIntent
{
	public IntentType Type { get; set; }

	public string? RawText { get; set; }

	public Dictionary<string, string> Parameters { get; set; } = new Dictionary<string, string>();
}
