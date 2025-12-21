using System;
using System.Threading.Tasks;
using AeroAI.Atc;
using AeroAI.Config;
using AeroAI.Models;

namespace AeroAI.Examples;

public static class OpenAiAtcDemo
{
	public static async Task RunDemoAsync(string apiKey, string model = "gpt-4o-mini")
	{
		Console.WriteLine("=== AeroAI OpenAI-Powered ATC Demo ===\n");
        EnvironmentConfig.Load();
        using var generator = new OpenAiAtcResponseGenerator(apiKey, model);
		FlightContext context = new FlightContext
		{
			CurrentPhase = FlightPhase.Preflight_Clearance,
			Callsign = "CJ",
			OriginIcao = "ELLX",
			DestinationIcao = "GMMN",
			CruiseFlightLevel = 350,
			SquawkCode = "4672"
		};
		context.DepartureRunway = new NavRunwaySummary
		{
			AirportIcao = "ELLX",
			RunwayIdentifier = "24",
			TrueHeadingDegrees = 240,
			LengthFeet = 12000,
			HasIlsOrLocalizer = true,
			HasRnavApproach = true
		};
        AeroAiLlmSession session = new AeroAiLlmSession(generator, context);
		string pilotTransmission = "Good evening Clearance this is CJ at stand 45 requesting IFR clearance to Casablanca as filed";
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
                        // generator disposed via using declaration.
                }
        }
}
