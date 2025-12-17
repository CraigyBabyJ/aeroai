using System.Diagnostics;
using AeroAI.Data;

namespace AeroAI.Atc;

/// <summary>
/// Minimal sanity checks for clearance routing top-down logic.
/// These are lightweight debug-time assertions and are not executed in release builds.
/// </summary>
internal static class ClearanceRoutingTests
{
#if DEBUG
	public static void Run()
	{
		// Ground should handle when no dedicated clearance frequency exists.
		var groundOnly = new AirportFrequencySet { Ground = 121.9 };
		var groundResult = ClearanceUnitResolver.ResolveForClearance("TEST", groundOnly);
		Debug.Assert(groundResult.Unit == AtcUnit.Ground && groundResult.HasAtc, "Expected Ground to handle clearance when only ground is present.");

		// When no frequencies are published, fall back to UNICOM.
		var noneResult = ClearanceUnitResolver.ResolveForClearance("XXXX", new AirportFrequencySet());
		Debug.Assert(!noneResult.HasAtc && noneResult.IsUnicom && noneResult.FrequencyMhz == 122.800, "Expected UNICOM fallback with 122.800.");
	}
#endif
}

