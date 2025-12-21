using System;
using System.Threading.Tasks;
using AeroAI.Atc;
using AeroAI.Models;

namespace AeroAI.Examples;

public static class LlmAtcDemo
{
	public static async Task RunDemoAsync()
	{
		Console.WriteLine("=== AeroAI LLM-Powered ATC Demo ===\n");
        var generator = new TemplateAtcResponseGenerator();
		FlightContext context = new FlightContext
		{
			CurrentPhase = FlightPhase.Preflight_Clearance,
			Callsign = "AeroAI123",
			OriginIcao = "EDDM",
			DestinationIcao = "LOWI",
			CruiseFlightLevel = 330,
			SquawkCode = "4672"
		};
		context.DepartureRunway = new NavRunwaySummary
		{
			AirportIcao = "EDDM",
			RunwayIdentifier = "26R",
			TrueHeadingDegrees = 260,
			LengthFeet = 12000,
			HasIlsOrLocalizer = true,
			HasRnavApproach = true
		};
        AeroAiLlmSession session = new AeroAiLlmSession(generator, context);
		string pilotTransmission = "Munich Clearance, AeroAI one two three, IFR to Innsbruck, ready to copy.";
		Console.WriteLine("PILOT → " + pilotTransmission + "\n");
		try
		{
			Console.WriteLine("ATC → " + await session.HandlePilotTransmissionAsync(pilotTransmission) + "\n");
		}
		catch (Exception ex)
		{
			Exception ex2 = ex;
			Console.WriteLine("ERROR: " + ex2.Message);
			if (ex2.InnerException != null)
			{
				Console.WriteLine("Inner exception: " + ex2.InnerException.Message);
			}
		}
                finally
                {
                        // template generator has no disposable resources.
                }
        }
}
