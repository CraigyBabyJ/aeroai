using AeroAI.Data;
using AeroAI.Logic;
using AeroAI.Models;
using AeroAI.Atc;
using AeroAI.Atc.Vectoring;
using AeroAI.Llm;
using AtcNavDataDemo.Atc;
using AtcNavDataDemo.Config;
using AtcNavDataDemo.SimBrief;
using AtcNavDataDemo.Weather;
using System.Text.Json;

namespace AtcNavDataDemo;

public class Program
{
    private const string NavDataPath =
        @"C:\\Users\\craig\\AppData\\Local\\Packages\\Microsoft.FlightSimulator_8wekyb3d8bbwe\\LocalCache\\Packages\\Community\pmdg-aircraft-737\\Config\\NavData\\e_dfd_PMDG.s3db";

    public static async Task Main()
    {
        Console.WriteLine("=== AeroAI Clearance Delivery ===");
        Console.WriteLine("Contacting clearance delivery for IFR clearance...");
        Console.WriteLine();

        var config = UserConfigStore.Load();

        if (!File.Exists(NavDataPath))
        {
            Console.WriteLine("NavData database not found:");
            Console.WriteLine(NavDataPath);
            Console.WriteLine("Please verify the path and try again.");
            return;
        }

        // Initialize data repository
        var navDataRepo = new SqliteNavDataRepository(NavDataPath);

        // Initialize selectors
        var runwaySelector = new RunwaySelector();
        var procedureSelector = new ProcedureSelector();

        string? callsign = null;
        string? originIcao = null;
        string? destinationIcao = null;
        EnrouteRoute? enrouteRoute = null;

        // === STEP 1: Load SimBrief Flight Plan (Automatic) ===
        string? simBriefId = config.SimBriefUsername;
        
        if (string.IsNullOrWhiteSpace(simBriefId))
        {
            Console.Write("Enter SimBrief username or pilot ID: ");
            simBriefId = Console.ReadLine()?.Trim();
        }
        else
        {
            Console.WriteLine($"Using SimBrief ID: {simBriefId}");
            Console.Write("Press Enter to use this ID, or type a different one: ");
            var input = Console.ReadLine();
            if (!string.IsNullOrWhiteSpace(input))
                simBriefId = input.Trim();
        }

        if (!string.IsNullOrWhiteSpace(simBriefId))
        {
            try
            {
                using var sbClient = new SimBriefClient();
                var ofp = await sbClient.FetchLatestFlightPlanAsync(simBriefId);

                if (ofp is not null)
                {
                    config.SimBriefUsername = simBriefId;
                    UserConfigStore.Save(config);

                    callsign = string.IsNullOrWhiteSpace(ofp.Callsign) ? null : ofp.Callsign;
                    originIcao = ofp.OriginIcao;
                    destinationIcao = ofp.DestinationIcao;

                    // Use waypoints from SimBrief navlog (already parsed)
                    // NOTE: We ONLY use SimBrief for enroute waypoints - NOT for runways/SID/STAR
                    enrouteRoute = new EnrouteRoute
                    {
                        OriginIcao = originIcao,
                        DestinationIcao = destinationIcao,
                        WaypointIdentifiers = ofp.WaypointIdentifiers
                    };

                    Console.WriteLine($"✓ Loaded SimBrief flight plan: {originIcao} → {destinationIcao}");
                    if (!string.IsNullOrWhiteSpace(callsign))
                        Console.WriteLine($"✓ Callsign: {callsign}");
                    if (enrouteRoute.WaypointIdentifiers.Count > 0)
                        Console.WriteLine($"✓ Enroute waypoints: {string.Join(" ", enrouteRoute.WaypointIdentifiers.Take(5))}... ({enrouteRoute.WaypointIdentifiers.Count} total)");
                }
                else
                {
                    Console.WriteLine("✗ Failed to load SimBrief OFP. Exiting.");
                    return;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"✗ Error contacting SimBrief: {ex.Message}");
                Console.WriteLine("Exiting.");
                return;
            }
        }
        else
        {
            Console.WriteLine("SimBrief ID required. Exiting.");
            return;
        }

        if (string.IsNullOrWhiteSpace(originIcao) || string.IsNullOrWhiteSpace(destinationIcao))
        {
            Console.WriteLine("Flight plan incomplete. Exiting.");
            return;
        }

        // === STEP 2: Get Weather Data from API ===
        Console.WriteLine();
        Console.WriteLine("Fetching current weather from checkwx API...");
        
        // Load checkwx API key
        string? checkWxApiKey = null;
        try
        {
            if (File.Exists("checkwx.json"))
            {
                var checkWxJson = File.ReadAllText("checkwx.json");
                var checkWxConfig = JsonSerializer.Deserialize<JsonElement>(checkWxJson);
                if (checkWxConfig.TryGetProperty("ApiKey", out var key))
                    checkWxApiKey = key.GetString();
            }
        }
        catch
        {
            // Ignore errors loading config
        }

        WeatherInfo originWeather;
        WeatherInfo destinationWeather;

        if (!string.IsNullOrWhiteSpace(checkWxApiKey))
        {
            using var wxClient = new CheckWxClient(checkWxApiKey);
            
            Console.Write($"Fetching weather for {originIcao}... ");
            var originWx = await wxClient.GetWeatherAsync(originIcao);
            if (originWx is not null)
            {
                originWeather = originWx;
                Console.WriteLine($"✓ Wind {originWeather.WindDirectionDegrees}°/{originWeather.WindSpeedKnots}kt, Vis {originWeather.VisibilityMeters}m, Ceiling {originWeather.CeilingFeet}ft");
            }
            else
            {
                Console.WriteLine("✗ API returned no data, using defaults");
                originWeather = GetDefaultWeather(originIcao);
            }

            Console.Write($"Fetching weather for {destinationIcao}... ");
            var destWx = await wxClient.GetWeatherAsync(destinationIcao);
            if (destWx is not null)
            {
                destinationWeather = destWx;
                Console.WriteLine($"✓ Wind {destinationWeather.WindDirectionDegrees}°/{destinationWeather.WindSpeedKnots}kt, Vis {destinationWeather.VisibilityMeters}m, Ceiling {destinationWeather.CeilingFeet}ft");
            }
            else
            {
                Console.WriteLine("✗ API returned no data, using defaults");
                destinationWeather = GetDefaultWeather(destinationIcao);
            }
        }
        else
        {
            Console.WriteLine("No checkwx API key found in checkwx.json, using default weather");
            originWeather = GetDefaultWeather(originIcao);
            destinationWeather = GetDefaultWeather(destinationIcao);
        }

        // === STEP 3: Get Aircraft Performance Profile (Defaults) ===
        var aircraft = GetAircraftProfile();

        // === STEP 4: Get Callsign ===
        if (string.IsNullOrWhiteSpace(callsign))
        {
            Console.Write("Enter callsign: ");
            callsign = Console.ReadLine()?.Trim().ToUpperInvariant() ?? "N123AB";
        }

        // === STEP 5: Automatic ATC Decision Making ===
        // IMPORTANT: We IGNORE SimBrief's departure/arrival runway selections.
        // AeroAI determines runways based on:
        // 1. Navdata (tbl_runways: headings, lengths, ILS/RNAV availability)
        // 2. Weather/wind (headwind vs tailwind, crosswind limits)
        // 3. Aircraft performance (min runway length, tailwind/crosswind limits)
        Console.WriteLine();
        Console.WriteLine("Determining active runway and procedures...");
        Console.WriteLine("(Using weather-based selection, ignoring SimBrief runway data)");

        // Get available runways from navdata
        var originRunways = navDataRepo.GetRunways(originIcao);
        var destinationRunways = navDataRepo.GetRunways(destinationIcao);

        if (originRunways.Count == 0)
        {
            Console.WriteLine($"No runways found for {originIcao}. Exiting.");
            return;
        }

        if (destinationRunways.Count == 0)
        {
            Console.WriteLine($"No runways found for {destinationIcao}. Exiting.");
            return;
        }

        // Select departure runway based on wind/weather/aircraft (NOT SimBrief)
        var departureRunwayResult = runwaySelector.SelectDepartureRunway(
            originIcao,
            originWeather,
            aircraft,
            originRunways);

        if (departureRunwayResult.SelectedRunway is null)
        {
            Console.WriteLine("✗ Cannot determine departure runway. Exiting.");
            return;
        }

        Console.WriteLine($"  → Departure runway {departureRunwayResult.SelectedRunway.RunwayIdentifier} selected based on wind {originWeather.WindDirectionDegrees}°/{originWeather.WindSpeedKnots}kt");

        // Select arrival runway based on wind/weather/aircraft (NOT SimBrief)
        var arrivalRunwayResult = runwaySelector.SelectArrivalRunway(
            destinationIcao,
            destinationWeather,
            aircraft,
            destinationRunways);

        if (arrivalRunwayResult.SelectedRunway is null)
        {
            Console.WriteLine("✗ Cannot determine arrival runway. Exiting.");
            return;
        }

        Console.WriteLine($"  → Arrival runway {arrivalRunwayResult.SelectedRunway.RunwayIdentifier} selected based on wind {destinationWeather.WindDirectionDegrees}°/{destinationWeather.WindSpeedKnots}kt");

        // Select SID based on selected runway and enroute waypoints (from SimBrief)
        // Note: We use SimBrief ONLY for enroute waypoints, NOT for runway/SID selection
        var availableSids = navDataRepo.GetSids(originIcao);
        var sidResult = procedureSelector.SelectSidForRoute(
            originIcao,
            departureRunwayResult.SelectedRunway,  // AeroAI-selected runway
            enrouteRoute ?? new EnrouteRoute { OriginIcao = originIcao, DestinationIcao = destinationIcao },
            availableSids);

        // Select STAR based on selected runway and enroute waypoints (from SimBrief)
        // Note: We use SimBrief ONLY for enroute waypoints, NOT for runway/STAR selection
        var availableStars = navDataRepo.GetStars(destinationIcao);
        var starResult = procedureSelector.SelectStarForRoute(
            destinationIcao,
            arrivalRunwayResult.SelectedRunway,  // AeroAI-selected runway
            enrouteRoute ?? new EnrouteRoute { OriginIcao = originIcao, DestinationIcao = destinationIcao },
            availableStars);

        // Select Approach based on selected runway, weather, and STAR
        var availableApproaches = navDataRepo.GetApproaches(destinationIcao);
        var approachResult = procedureSelector.SelectApproachForRunway(
            destinationIcao,
            arrivalRunwayResult.SelectedRunway,
            destinationWeather,
            availableApproaches,
            starResult);

        // === STEP 6: Interactive ATC Session (SayIntentions.ai / BeyondATC style) ===
        var cruiseFl = enrouteRoute?.WaypointIdentifiers.Count > 0 
            ? Math.Min(330, 200 + (enrouteRoute.WaypointIdentifiers.Count * 10))
            : 330;
        
        Console.WriteLine();
        Console.WriteLine("═══════════════════════════════════════════════════════════════");
        Console.WriteLine("           INTERACTIVE ATC - TYPE YOUR PILOT TRANSMISSIONS");
        Console.WriteLine("═══════════════════════════════════════════════════════════════");
        Console.WriteLine();
        Console.WriteLine("Instructions:");
        Console.WriteLine("  - Type your pilot transmissions and press Enter");
        Console.WriteLine("  - ATC will respond based on flight phase and context");
        Console.WriteLine("  - Type 'exit' to end the session");
        Console.WriteLine();
        Console.WriteLine($"Selected Runways (from navdata + weather):");
        Console.WriteLine($"  Departure: {departureRunwayResult.SelectedRunway?.RunwayIdentifier ?? "N/A"} (wind {originWeather.WindDirectionDegrees}°/{originWeather.WindSpeedKnots}kt)");
        Console.WriteLine($"  Arrival: {arrivalRunwayResult.SelectedRunway?.RunwayIdentifier ?? "N/A"} (wind {destinationWeather.WindDirectionDegrees}°/{destinationWeather.WindSpeedKnots}kt)");
        Console.WriteLine();
        Console.WriteLine("═══════════════════════════════════════════════════════════════");
        Console.WriteLine();

        // Load environment config for OpenAI
        AeroAI.Config.EnvironmentConfig.Load();

        // Create OpenAI LLM client for ATC responses
        var llm = new AeroAI.Llm.OpenAiLlmClient(
            apiKey: AeroAI.Config.EnvironmentConfig.GetOpenAiApiKey(),
            model: AeroAI.Config.EnvironmentConfig.GetOpenAiModel(),
            baseUrl: AeroAI.Config.EnvironmentConfig.GetOpenAiBaseUrl()
        );

        // Create flight context for LLM session
        var flightContext = new FlightContext
        {
            Callsign = callsign ?? "UNKNOWN",
            OriginIcao = originIcao,
            DestinationIcao = destinationIcao,
            EnrouteRoute = enrouteRoute,
            OriginWeather = originWeather,
            DestinationWeather = destinationWeather,
            Aircraft = aircraft,
            CurrentPhase = FlightPhase.Preflight_Clearance,
            CurrentAtcUnit = AtcUnit.ClearanceDelivery,
            DepartureRunway = departureRunwayResult.SelectedRunway,
            ArrivalRunway = arrivalRunwayResult.SelectedRunway,
            SelectedSid = sidResult,
            SelectedStar = starResult,
            SelectedApproach = approachResult,
            CruiseFlightLevel = cruiseFl
        };

        // Verify critical data is set
        if (string.IsNullOrWhiteSpace(flightContext.DestinationIcao))
        {
            Console.WriteLine();
            Console.WriteLine("⚠ WARNING: DestinationIcao is not set in FlightContext!");
            Console.WriteLine("  This will prevent IFR clearance from being issued.");
            Console.WriteLine("  SimBrief data may not have loaded correctly.");
            Console.WriteLine();
        }
        else
        {
            Console.WriteLine($"✓ FlightContext initialized: Origin={flightContext.OriginIcao}, Destination={flightContext.DestinationIcao}");
        }

        // Create LLM-powered ATC session
        var atcSession = new AeroAiLlmSession(llm, flightContext);

        // Check if API logging is enabled
        var logApi = Environment.GetEnvironmentVariable("AEROAI_LOG_API");
        if (!string.IsNullOrWhiteSpace(logApi) && 
            (logApi.Equals("1", StringComparison.OrdinalIgnoreCase) ||
             logApi.Equals("true", StringComparison.OrdinalIgnoreCase) ||
             logApi.Equals("yes", StringComparison.OrdinalIgnoreCase)))
        {
            Console.WriteLine();
            Console.WriteLine("⚠ API REQUEST/RESPONSE LOGGING ENABLED");
            Console.WriteLine("   Set AEROAI_LOG_API=0 to disable");
            Console.WriteLine();
        }

        // Interactive loop
        while (true)
        {
            Console.Write("PILOT → ");
            var pilotInput = Console.ReadLine();
            
            if (string.IsNullOrWhiteSpace(pilotInput))
                continue;
            
            if (pilotInput.Trim().Equals("exit", StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine("Ending ATC session. Good day!");
                break;
            }

            try
            {
                // Check if clearance can be auto-issued (data became ready)
                var autoResponse = await atcSession.CheckAndAutoIssueClearanceAsync();
                if (autoResponse is not null)
                {
                    Console.WriteLine($"ATC → {autoResponse}");
                    continue;
                }

                var atcResponse = await atcSession.HandlePilotTransmissionAsync(pilotInput);
                if (atcResponse is not null)
                {
                    Console.WriteLine($"ATC → {atcResponse}");
                }
                else if (!string.IsNullOrWhiteSpace(pilotInput))
                {
                    // If null but pilot said something, it was filtered (acknowledgment) - show state for debugging
                    // Console.WriteLine($"[DEBUG: Message filtered, state={atcSession.GetState()}]");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"ATC → Error: {ex.Message}");
                if (ex.InnerException is not null)
                {
                    Console.WriteLine($"  {ex.InnerException.Message}");
                }
            }
            Console.WriteLine();
        }
    }

    private static EnrouteRoute ParseEnrouteRoute(string routeString, string originIcao, string destinationIcao)
    {
        if (string.IsNullOrWhiteSpace(routeString))
            return new EnrouteRoute { OriginIcao = originIcao, DestinationIcao = destinationIcao };

        // Split route by spaces and filter out common non-waypoint tokens
        var tokens = routeString.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        var waypoints = new List<string>();

        // Common tokens to ignore
        var ignoreTokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "DCT", "DIRECT", "SID", "STAR", "VIA", "TO", "FROM"
        };

        foreach (var token in tokens)
        {
            var trimmed = token.Trim();
            if (string.IsNullOrWhiteSpace(trimmed))
                continue;

            // Skip if it's a known non-waypoint token
            if (ignoreTokens.Contains(trimmed))
                continue;

            waypoints.Add(trimmed.ToUpperInvariant());
        }

        return new EnrouteRoute
        {
            OriginIcao = originIcao,
            DestinationIcao = destinationIcao,
            WaypointIdentifiers = waypoints
        };
    }

    private static WeatherInfo GetDefaultWeather(string airportIcao)
    {
        // Default VFR weather
        return new WeatherInfo
        {
            AirportIcao = airportIcao,
            WindDirectionDegrees = 270,
            WindSpeedKnots = 10,
            VisibilityMeters = 10000,
            CeilingFeet = 5000,
            IsIfr = false,
            IsLowVisibility = false
        };
    }

    private static AircraftPerformanceProfile GetAircraftProfile()
    {
        // Use default B738 performance (no user input needed for clearance delivery)
        return new AircraftPerformanceProfile
        {
            IcaoType = "B738",
            RequiredTakeoffDistanceFeet = 8000,
            RequiredLandingDistanceFeet = 6000,
            MaxTailwindComponentKnots = 10,
            MaxCrosswindComponentKnots = 25
        };
    }
}

