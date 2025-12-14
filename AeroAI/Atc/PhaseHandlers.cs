using System;
using System.Threading;
using System.Threading.Tasks;
using AeroAI.Llm;

namespace AeroAI.Atc;

public static class PhaseHandlers
{
	public static async Task<string?> HandleClearancePhase(string pilotText, AtcContext context, FlightContext flightContext, AeroAiPhraseEngine phraseEngine, CancellationToken ct = default(CancellationToken))
	{
		if (IsAck(pilotText))
		{
			return null;
		}

		try
		{
			if (ClearanceHelpers.IsNonOperationalAck(pilotText))
			{
				return null;
			}

			bool ready = ClearanceHelpers.ClearanceDataComplete(context);

			if (!ready && !context.StateFlags.IfrClearanceIssued)
			{
				context.ClearanceDecision.ClearanceType = "INFORMATION_ONLY";
				context.Permissions.AllowIfrClearance = false;
				return (await phraseEngine.GenerateAtcTransmissionAsync(context, pilotText, ct)).Trim();
			}

			if (ready && !context.StateFlags.IfrClearanceIssued)
			{
				context.ClearanceDecision.ClearanceType = "IFR_CLEARANCE";
				context.Permissions.AllowIfrClearance = true;
				string atc = (await phraseEngine.GenerateAtcTransmissionAsync(context, pilotText, ct)).Trim();
				context.StateFlags.IfrClearanceIssued = true;
				flightContext.CurrentAtcState = AtcState.ClearanceIssued;
				return atc;
			}

			if (context.StateFlags.IfrClearanceIssued)
			{
				context.ClearanceDecision.ClearanceType = "INFORMATION_ONLY";
				context.Permissions.AllowIfrClearance = false;
				return (await phraseEngine.GenerateAtcTransmissionAsync(context, pilotText, ct)).Trim();
			}

			return null;
		}
		catch (Exception ex)
		{
			Console.Error.WriteLine("ERROR in HandleClearancePhase: " + ex.Message);
			return null;
		}
	}

	public static async Task<string?> HandleTaxiOutPhase(string pilotText, AtcContext context, FlightContext flightContext, AeroAiPhraseEngine phraseEngine, CancellationToken ct = default(CancellationToken))
	{
		try
		{
			context.ClearanceDecision.ClearanceType = "TAXI";
			context.Permissions.AllowTaxi = true;
			return (await phraseEngine.GenerateAtcTransmissionAsync(context, pilotText, ct)).Trim();
		}
		catch (Exception ex)
		{
			Exception ex2 = ex;
			Console.Error.WriteLine("ERROR in HandleTaxiOutPhase: " + ex2.Message);
			return null;
		}
	}

	public static async Task<string?> HandleLineupTakeoffPhase(string pilotText, AtcContext context, FlightContext flightContext, AeroAiPhraseEngine phraseEngine, CancellationToken ct = default(CancellationToken))
	{
		try
		{
			string lower = pilotText.ToLowerInvariant();
			if (lower.Contains("ready") || lower.Contains("holding short"))
			{
				context.ClearanceDecision.ClearanceType = "LINEUP";
				context.Permissions.AllowLineup = true;
			}
			else if (lower.Contains("line up") || lower.Contains("lineup"))
			{
				context.ClearanceDecision.ClearanceType = "TAKEOFF";
				context.Permissions.AllowTakeoffClearance = true;
			}
			return (await phraseEngine.GenerateAtcTransmissionAsync(context, pilotText, ct)).Trim();
		}
		catch (Exception ex)
		{
			Exception ex2 = ex;
			Console.Error.WriteLine("ERROR in HandleLineupTakeoffPhase: " + ex2.Message);
			return null;
		}
	}

	public static async Task<string?> HandleDepartureClimbPhase(string pilotText, AtcContext context, FlightContext flightContext, AeroAiPhraseEngine phraseEngine, CancellationToken ct = default(CancellationToken))
	{
		try
		{
			context.ClearanceDecision.ClearanceType = "CLIMB";
			return (await phraseEngine.GenerateAtcTransmissionAsync(context, pilotText, ct)).Trim();
		}
		catch (Exception ex)
		{
			Exception ex2 = ex;
			Console.Error.WriteLine("ERROR in HandleDepartureClimbPhase: " + ex2.Message);
			return null;
		}
	}

	public static async Task<string?> HandleEnroutePhase(string pilotText, AtcContext context, FlightContext flightContext, AeroAiPhraseEngine phraseEngine, CancellationToken ct = default(CancellationToken))
	{
		try
		{
			context.ClearanceDecision.ClearanceType = "INFORMATION_ONLY";
			return (await phraseEngine.GenerateAtcTransmissionAsync(context, pilotText, ct)).Trim();
		}
		catch (Exception ex)
		{
			Exception ex2 = ex;
			Console.Error.WriteLine("ERROR in HandleEnroutePhase: " + ex2.Message);
			return null;
		}
	}

	public static async Task<string?> HandleArrivalPhase(string pilotText, AtcContext context, FlightContext flightContext, AeroAiPhraseEngine phraseEngine, CancellationToken ct = default(CancellationToken))
	{
		try
		{
			context.ClearanceDecision.ClearanceType = "DESCENT";
			return (await phraseEngine.GenerateAtcTransmissionAsync(context, pilotText, ct)).Trim();
		}
		catch (Exception ex)
		{
			Exception ex2 = ex;
			Console.Error.WriteLine("ERROR in HandleArrivalPhase: " + ex2.Message);
			return null;
		}
	}

	public static async Task<string?> HandleApproachPhase(string pilotText, AtcContext context, FlightContext flightContext, AeroAiPhraseEngine phraseEngine, CancellationToken ct = default(CancellationToken))
	{
		try
		{
			context.ClearanceDecision.ClearanceType = "APPROACH";
			context.Permissions.AllowApproachClearance = true;
			return (await phraseEngine.GenerateAtcTransmissionAsync(context, pilotText, ct)).Trim();
		}
		catch (Exception ex)
		{
			Exception ex2 = ex;
			Console.Error.WriteLine("ERROR in HandleApproachPhase: " + ex2.Message);
			return null;
		}
	}

	public static async Task<string?> HandleLandingPhase(string pilotText, AtcContext context, FlightContext flightContext, AeroAiPhraseEngine phraseEngine, CancellationToken ct = default(CancellationToken))
	{
		try
		{
			context.ClearanceDecision.ClearanceType = "LANDING";
			context.Permissions.AllowLandingClearance = true;
			return (await phraseEngine.GenerateAtcTransmissionAsync(context, pilotText, ct)).Trim();
		}
		catch (Exception ex)
		{
			Exception ex2 = ex;
			Console.Error.WriteLine("ERROR in HandleLandingPhase: " + ex2.Message);
			return null;
		}
	}

	public static async Task<string?> HandleTaxiInPhase(string pilotText, AtcContext context, FlightContext flightContext, AeroAiPhraseEngine phraseEngine, CancellationToken ct = default(CancellationToken))
	{
		try
		{
			context.ClearanceDecision.ClearanceType = "TAXI";
			context.Permissions.AllowTaxi = true;
			return (await phraseEngine.GenerateAtcTransmissionAsync(context, pilotText, ct)).Trim();
		}
		catch (Exception ex)
		{
			Exception ex2 = ex;
			Console.Error.WriteLine("ERROR in HandleTaxiInPhase: " + ex2.Message);
			return null;
		}
	}

	private static bool IsAck(string s)
	{
		if (string.IsNullOrWhiteSpace(s))
		{
			return true;
		}

		string t = s.Trim().ToLowerInvariant();
		return t.Contains("standby") || t.Contains("roger") || t.Contains("copy") || t.Contains("wilco") || t.Contains("ok");
	}
}
