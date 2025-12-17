using System.Diagnostics;
using AeroAI.Data;
using AeroAI.Llm;

namespace AeroAI.Atc;

/// <summary>
/// Debug-only checks to ensure destination confirmation flow preserves state and proceeds after confirmation.
/// </summary>
internal static class PendingConfirmationTests
{
#if DEBUG
	public static async Task RunAsync()
	{
		// Arrange: create a context with enough data to allow deterministic clearance.
		var ctx = new FlightContext
		{
			Callsign = "EZY113",
			RawCallsign = "EZY113",
			RadioCallsign = "Easy 113",
			OriginIcao = "EGPH",
			DestinationIcao = "EGKK",
			DestinationName = "Gatwick",
			DepartureRunway = new NavRunwaySummary { AirportIcao = "EGPH", RunwayIdentifier = "24" },
			ClearedAltitude = 5000,
			SquawkCode = "1726",
			CruiseFlightLevel = 330,
			CurrentPhase = FlightPhase.Preflight_Clearance,
			CurrentAtcUnit = AtcUnit.ClearanceDelivery
		};

		// Use a dummy OpenAI client; no calls are made because clearance is deterministic.
		var dummyLlm = new OpenAiLlmClient("sk-test", baseUrl: "https://api.invalid");
		var session = new AeroAiLlmSession(dummyLlm, ctx);

		// Act: pilot requests clearance to mismatched destination -> prompt for confirmation.
		var first = await session.HandlePilotTransmissionAsync("Easy 113 requesting clearance to Stansted");
		Debug.Assert(first != null && first.ToLowerInvariant().Contains("confirm destination"), "Expected destination confirmation prompt.");
		Debug.Assert(session.HasPendingConfirmation, "Pending confirmation should remain active after mismatch.");

		// Act: pilot confirms filed destination -> clearance proceeds, pending cleared.
		var second = await session.HandlePilotTransmissionAsync("Destination is Gatwick");
		Debug.Assert(second != null && second.ToLowerInvariant().Contains("cleared to"), "Expected clearance issuance after confirmation.");
		Debug.Assert(!session.HasPendingConfirmation, "Pending confirmation should clear after matching destination.");

		// Ensure no callsign nagging appears in session responses.
		Debug.Assert(second != null && !second.Contains("Who is calling?", StringComparison.OrdinalIgnoreCase), "Should not prompt for callsign during confirmation.");
	}
#endif
}

