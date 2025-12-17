using System;

namespace AeroAI.Config;

/// <summary>
/// Training mode configuration flags for ATC behavior.
/// </summary>
public static class TrainingConfig
{
	/// <summary>
	/// If true, require ATIS confirmation before issuing IFR clearance (training behavior).
	/// Default: true
	/// </summary>
	public static bool StrictAtisForClearance
	{
		get
		{
			var envVar = Environment.GetEnvironmentVariable("AEROAI_TRAINING_STRICT_ATIS");
			if (string.IsNullOrWhiteSpace(envVar))
				return true; // Default to strict mode

			return envVar.Equals("true", StringComparison.OrdinalIgnoreCase) ||
			       envVar.Equals("1", StringComparison.OrdinalIgnoreCase) ||
			       envVar.Equals("yes", StringComparison.OrdinalIgnoreCase);
		}
	}

	/// <summary>
	/// If true, require explicit collection of all training slots (aircraft type, stand/gate, ATIS, IFR request) before issuing clearance.
	/// Default: true
	/// </summary>
	public static bool StrictClearanceData
	{
		get
		{
			var envVar = Environment.GetEnvironmentVariable("AEROAI_TRAINING_STRICT_CLEARANCE_DATA");
			if (string.IsNullOrWhiteSpace(envVar))
				return true; // Default to strict mode

			return envVar.Equals("true", StringComparison.OrdinalIgnoreCase) ||
			       envVar.Equals("1", StringComparison.OrdinalIgnoreCase) ||
			       envVar.Equals("yes", StringComparison.OrdinalIgnoreCase);
		}
	}
}

