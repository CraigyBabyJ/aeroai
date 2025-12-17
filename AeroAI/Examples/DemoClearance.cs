using System;
using System.IO;
using System.Threading.Tasks;
using AeroAI.Atc;
using AeroAI.Config;
using AeroAI.Llm;
using AeroAI.Models;

namespace AeroAI.Examples;

public static class DemoClearance
{
	public static async Task RunAsync()
	{
		Console.WriteLine("=== AeroAI Clearance Delivery Demo ===\n");
		EnvironmentConfig.Load();
		try
		{
			string apiKey = EnvironmentConfig.GetOpenAiApiKey();
			string model = EnvironmentConfig.GetOpenAiModel();
			string baseUrl = EnvironmentConfig.GetOpenAiBaseUrl();
			string systemPromptPath = EnvironmentConfig.GetSystemPromptPath();
			using AeroAiPhraseEngine phraseEngine = new AeroAiPhraseEngine(apiKey, model, baseUrl, systemPromptPath);
			AtcContext context = new AtcContext
			{
				ControllerRole = "CLEARANCE",
				Phase = "CLEARANCE",
				FlightInfo = new FlightInfo
				{
					Callsign = "CJ",
					AircraftType = "B738",
					DepIcao = "EGCC",
					DepName = "Manchester",
					ArrIcao = "GMMN",
					ArrName = "Casablanca",
					CruiseLevel = "FL350"
				},
				ClearanceDecision = new ClearanceDecision
				{
					ClearanceType = "IFR_CLEARANCE",
					ClearedTo = "Casablanca",
					RouteSummary = "as filed",
					DepRunway = "23R",
					Sid = "MAN1A",
					ViaRadarVectors = false,
					InitialAltitudeFt = 5000,
					Squawk = "4672"
				},
				WeatherRelevant = new WeatherRelevant
				{
					DepWindDir = 230,
					DepWindKt = 12,
					QnhHpa = 1015
				},
				StateFlags = new StateFlags
				{
					IfrClearanceIssued = false,
					TaxiClearanceIssued = false,
					LineupIssued = false,
					TakeoffIssued = false,
					ApproachIssued = false,
					LandingIssued = false
				},
				Permissions = new Permissions
				{
					AllowIfrClearance = true,
					AllowTaxi = false,
					AllowLineup = false,
					AllowTakeoffClearance = false,
					AllowApproachClearance = false,
					AllowLandingClearance = false
				}
			};
			FlightContext flightContext = new FlightContext
			{
				Callsign = "CJ",
				RadioCallsign = "CJ",
				OriginIcao = "EGCC",
				OriginName = "Manchester",
				DestinationIcao = "GMMN",
				DestinationName = "Casablanca",
				CruiseFlightLevel = 350,
				ClearedAltitude = 5000,
				SquawkCode = "4672",
				DepartureRunway = new NavRunwaySummary
				{
					AirportIcao = "EGCC",
					RunwayIdentifier = "23R"
				}
			};
			string pilotTransmission = "Good evening Clearance, this is CJ at stand 45 requesting IFR clearance to Casablanca as filed";
			Console.WriteLine("PILOT → " + pilotTransmission + "\n");
			Console.WriteLine("Generating ATC response...\n");
			Console.WriteLine("ATC → " + await phraseEngine.GenerateAtcTransmissionAsync(context, pilotTransmission, flightContext) + "\n");
			Console.WriteLine("--- Context JSON (for debugging) ---");
			Console.WriteLine(context.ToJson());
		}
		catch (FileNotFoundException ex)
		{
			FileNotFoundException ex2 = ex;
			Console.WriteLine("ERROR: " + ex2.Message);
			Console.WriteLine("\nPlease ensure:");
			Console.WriteLine("1. .env file exists with OPENAI_API_KEY set");
			Console.WriteLine("2. prompts/aeroai_system_prompt.txt exists");
		}
		catch (InvalidOperationException ex3)
		{
			InvalidOperationException ex4 = ex3;
			Console.WriteLine("ERROR: " + ex4.Message);
		}
		catch (Exception ex5)
		{
			Exception ex6 = ex5;
			Console.WriteLine("ERROR: " + ex6.GetType().Name + ": " + ex6.Message);
			if (ex6.InnerException != null)
			{
				Console.WriteLine("Inner exception: " + ex6.InnerException.Message);
			}
		}
	}
}
