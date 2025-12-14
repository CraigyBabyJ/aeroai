using AeroAI.Data;
using AeroAI.Logic;
using AeroAI.Models;
using AeroAI.Atc;
using AeroAI.Atc.Vectoring;
using AeroAI.Llm;
using AeroAI.Audio;
using AtcNavDataDemo.Atc;
using AtcNavDataDemo.Config;
using AtcNavDataDemo.SimBrief;
using AtcNavDataDemo.Weather;
using System.Text.Json;
using System.Net.Http;
using System.Net;
using System.Net.Http;

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

        var airlineDirectory = AirlineDirectory.Load();
        if (!airlineDirectory.HasData)
        {
            Console.WriteLine("[CALLSIGN] airlines.json not loaded; airline names will not be expanded.");
        }

        // Initialize selectors
        var runwaySelector = new RunwaySelector();
        var procedureSelector = new ProcedureSelector();

        CallsignDetails? callsignDetails = null;
        string simbriefCallsignRaw = string.Empty;
        bool simBriefCallsignProvided = false;
        string? originIcao = null;
        string? destinationIcao = null;
        EnrouteRoute? enrouteRoute = null;
        string? simbriefPlannedDepRunway = null;
        string? simbriefPlannedArrRunway = null;
        string? simbriefPlannedSid = null;

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

                    var rawSimBriefCallsign = ofp.Callsign;
                    if (!string.IsNullOrWhiteSpace(ofp.Callsign) && !string.IsNullOrWhiteSpace(ofp.FlightNumber))
                    {
                        // If callsign is just the airline code, append the flight number.
                        if (ofp.Callsign.Trim().Length == 3 && ofp.Callsign.All(char.IsLetter))
                        {
                            rawSimBriefCallsign = $"{ofp.Callsign}{ofp.FlightNumber}";
                        }
                    }
                    else if (string.IsNullOrWhiteSpace(ofp.Callsign) && !string.IsNullOrWhiteSpace(ofp.FlightNumber))
                    {
                        // If callsign missing but flight number present, keep as-is so we can still prompt later.
                        rawSimBriefCallsign = ofp.FlightNumber;
                        if (!string.IsNullOrWhiteSpace(ofp.AirlineIcao))
                        {
                            rawSimBriefCallsign = $"{ofp.AirlineIcao}{ofp.FlightNumber}";
                        }
                    }
                    else if (!string.IsNullOrWhiteSpace(ofp.AirlineIcao) && !string.IsNullOrWhiteSpace(ofp.FlightNumber) && rawSimBriefCallsign == ofp.FlightNumber)
                    {
                        // Append airline ICAO if SimBrief returned only the number.
                        rawSimBriefCallsign = $"{ofp.AirlineIcao}{ofp.FlightNumber}";
                    }

                    simbriefCallsignRaw = rawSimBriefCallsign;
                    Console.WriteLine($"[CALLSIGN] SimBrief callsign raw: '{rawSimBriefCallsign}'");
                    callsignDetails = CallsignDetails.FromRaw(rawSimBriefCallsign, airlineDirectory);
                    simBriefCallsignProvided = callsignDetails is not null && !string.IsNullOrWhiteSpace(callsignDetails.Raw);
                    originIcao = ofp.OriginIcao;
                    destinationIcao = ofp.DestinationIcao;

                    if (callsignDetails is not null && callsignDetails.IsValid && string.IsNullOrWhiteSpace(callsignDetails.AirlineName))
                    {
                        Console.WriteLine($"[CALLSIGN] Airline ICAO {callsignDetails.AirlineIcao} not found in airlines.json; using raw callsign.");
                    }

                    // Use waypoints from SimBrief navlog (already parsed)
                    // NOTE: We ONLY use SimBrief for enroute waypoints - NOT for runways/SID/STAR
                    enrouteRoute = new EnrouteRoute
                    {
                        OriginIcao = originIcao,
                        DestinationIcao = destinationIcao,
                        WaypointIdentifiers = ofp.WaypointIdentifiers
                    };
                    simbriefPlannedDepRunway = ofp.PlannedDepartureRunway;
                    simbriefPlannedArrRunway = ofp.PlannedArrivalRunway;
                    simbriefPlannedSid = ofp.PlannedSid;

                    Console.WriteLine($"- Loaded SimBrief flight plan: {originIcao} -> {destinationIcao}");
                    Console.WriteLine($"  SimBrief planned: RWY {simbriefPlannedDepRunway}, SID {(string.IsNullOrWhiteSpace(simbriefPlannedSid) ? "(none)" : simbriefPlannedSid)}");
                    if (callsignDetails is not null)
                    {
                        if (simBriefCallsignProvided && !callsignDetails.IsValid && !string.IsNullOrWhiteSpace(callsignDetails.Raw))
                        {
                            Console.WriteLine($"? SimBrief callsign '{callsignDetails.Raw}' is not in airline ICAO + flight number format (AAA1234); using it as provided.");
                        }

                        var radioCallsign = string.IsNullOrWhiteSpace(callsignDetails.RadioCallsign)
                            ? callsignDetails.Raw
                            : callsignDetails.RadioCallsign;
                        if (!string.IsNullOrWhiteSpace(radioCallsign))
                        {
                            if (!string.Equals(callsignDetails.Raw, radioCallsign, StringComparison.OrdinalIgnoreCase))
                            {
                                Console.WriteLine($"- Callsign: {callsignDetails.Raw} (as {radioCallsign})");
                            }
                            else
                            {
                                Console.WriteLine($"- Callsign: {radioCallsign}");
                            }
                        }
                    }
                    if (enrouteRoute.WaypointIdentifiers.Count > 0)
                        Console.WriteLine($"- Enroute waypoints: {string.Join(" ", enrouteRoute.WaypointIdentifiers.Take(5))}... ({enrouteRoute.WaypointIdentifiers.Count} total)");
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
        string simbriefFallbackCallsign = callsignDetails?.CanonicalCallsign ?? string.Empty;

        if (callsignDetails is null || string.IsNullOrWhiteSpace(callsignDetails.Raw))
        {
            Console.Write("Enter callsign (airline ICAO + flight number, e.g. SAS1234): ");
            var manualCallsign = Console.ReadLine()?.Trim();
            if (string.IsNullOrWhiteSpace(manualCallsign))
            {
                // If we already had a partial SimBrief callsign, keep it; otherwise fallback.
                manualCallsign = !string.IsNullOrWhiteSpace(simbriefCallsignRaw)
                    ? simbriefCallsignRaw
                    : (!string.IsNullOrWhiteSpace(simbriefFallbackCallsign) ? simbriefFallbackCallsign : "N123AB");
            }
            callsignDetails = CallsignDetails.FromRaw(manualCallsign, airlineDirectory);
        }

        var displayCallsign = callsignDetails is not null && !string.IsNullOrWhiteSpace(callsignDetails.RadioCallsign)
            ? callsignDetails.RadioCallsign
            : (!string.IsNullOrWhiteSpace(callsignDetails?.CanonicalCallsign) ? callsignDetails?.CanonicalCallsign : callsignDetails?.Raw);

        if (string.IsNullOrWhiteSpace(displayCallsign))
        {
            displayCallsign = "N123AB";
        }

        callsignDetails ??= CallsignDetails.FromRaw(displayCallsign, airlineDirectory);

        // === STEP 5: Automatic ATC Decision Making ===
        // Runway/SID selection:
        // 1. Prefer SimBrief planned runway/SID if they pass weather/length checks.
        // 2. Otherwise, select runway from navdata + weather + aircraft limits,
        //    then select a compatible SID for that runway and the filed route.
        Console.WriteLine();
        Console.WriteLine("Determining active runway and procedures...");
        Console.WriteLine("(Preferring SimBrief planned runway if weather allows; otherwise weather-based)");

        // Get available runways from navdata
        var originRunways = navDataRepo.GetRunways(originIcao);
        var destinationRunways = navDataRepo.GetRunways(destinationIcao);

        if (originRunways.Count == 0)
        {
            Console.WriteLine($"No runways found for {originIcao}. Exiting.");
            return;
        }
        // Debug: Show actual runway identifiers in navdata
        Console.WriteLine($"  NavData runways for {originIcao}: {string.Join(", ", originRunways.Select(r => r.RunwayIdentifier))}");

        if (destinationRunways.Count == 0)
        {
            Console.WriteLine($"No runways found for {destinationIcao}. Exiting.");
            return;
        }
        Console.WriteLine($"  NavData runways for {destinationIcao}: {string.Join(", ", destinationRunways.Select(r => r.RunwayIdentifier))}");

        // Attempt to honor SimBrief planned departure runway if weather/aircraft allow.
        NavRunwaySummary? plannedDepartureRunway = null;
        if (!string.IsNullOrWhiteSpace(simbriefPlannedDepRunway))
        {
            // Try exact match first, then flexible match (24 vs RW24)
            plannedDepartureRunway = originRunways.FirstOrDefault(r =>
                string.Equals(r.RunwayIdentifier, simbriefPlannedDepRunway, StringComparison.OrdinalIgnoreCase));
            
            if (plannedDepartureRunway == null)
            {
                // Try matching with/without RW prefix
                plannedDepartureRunway = originRunways.FirstOrDefault(r =>
                    RunwayIdentifiersMatch(r.RunwayIdentifier, simbriefPlannedDepRunway));
            }
        }

        var plannedDepartureAccepted = plannedDepartureRunway != null &&
            runwaySelector.IsRunwayAcceptable(plannedDepartureRunway, originWeather, aircraft, isDeparture: true);

        RunwaySelectionResult departureRunwayResult;
        if (plannedDepartureAccepted)
        {
            departureRunwayResult = new RunwaySelectionResult
            {
                SelectedRunway = plannedDepartureRunway,
                CandidatesConsidered = originRunways,
                Reason = $"Using SimBrief planned departure runway {plannedDepartureRunway.RunwayIdentifier} (validated against weather)."
            };
            Console.WriteLine($"  Using SimBrief planned departure runway {plannedDepartureRunway.RunwayIdentifier} (passes weather/length checks).");
        }
        else
        {
            if (plannedDepartureRunway != null)
            {
                Console.WriteLine($"  SimBrief planned departure runway {plannedDepartureRunway.RunwayIdentifier} rejected due to wind/length/approach; selecting weather-based runway (may still pick the same runway if no better option).");
            }
            else if (!string.IsNullOrWhiteSpace(simbriefPlannedDepRunway))
            {
                Console.WriteLine($"  SimBrief planned departure runway '{simbriefPlannedDepRunway}' not found in navdata; selecting weather-based runway.");
            }

            departureRunwayResult = runwaySelector.SelectDepartureRunway(
                originIcao,
                originWeather,
                aircraft,
                originRunways);
        }

        if (departureRunwayResult.SelectedRunway is null)
        {
            Console.WriteLine("? Cannot determine departure runway. Exiting.");
            return;
        }

        if (!plannedDepartureAccepted &&
            plannedDepartureRunway != null &&
            departureRunwayResult.SelectedRunway.RunwayIdentifier.Equals(plannedDepartureRunway.RunwayIdentifier, StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine($"  Weather-based selection kept SimBrief runway {plannedDepartureRunway.RunwayIdentifier} despite prior rejection (likely no better alternative).");
        }

        Console.WriteLine($"  Departure runway {departureRunwayResult.SelectedRunway.RunwayIdentifier} selected (wind {originWeather.WindDirectionDegrees}deg/{originWeather.WindSpeedKnots}kt)");

        // Attempt to honor SimBrief planned arrival runway if weather/aircraft allow.
        NavRunwaySummary? plannedArrivalRunway = null;
        if (!string.IsNullOrWhiteSpace(simbriefPlannedArrRunway))
        {
            // Try exact match first, then flexible match
            plannedArrivalRunway = destinationRunways.FirstOrDefault(r =>
                string.Equals(r.RunwayIdentifier, simbriefPlannedArrRunway, StringComparison.OrdinalIgnoreCase));
            
            if (plannedArrivalRunway == null)
            {
                plannedArrivalRunway = destinationRunways.FirstOrDefault(r =>
                    RunwayIdentifiersMatch(r.RunwayIdentifier, simbriefPlannedArrRunway));
            }
        }

        RunwaySelectionResult arrivalRunwayResult;
        if (plannedArrivalRunway != null &&
            runwaySelector.IsRunwayAcceptable(plannedArrivalRunway, destinationWeather, aircraft, isDeparture: false))
        {
            arrivalRunwayResult = new RunwaySelectionResult
            {
                SelectedRunway = plannedArrivalRunway,
                CandidatesConsidered = destinationRunways,
                Reason = $"Using SimBrief planned arrival runway {plannedArrivalRunway.RunwayIdentifier} (validated against weather/approach)."
            };
            Console.WriteLine($"  ✓ Using SimBrief planned arrival runway {plannedArrivalRunway.RunwayIdentifier} (passes weather/length/approach checks).");
        }
        else
        {
            if (plannedArrivalRunway != null)
            {
                Console.WriteLine($"  ? SimBrief planned arrival runway {plannedArrivalRunway.RunwayIdentifier} rejected due to wind/length/approach; selecting weather-based runway.");
            }
            else if (!string.IsNullOrWhiteSpace(simbriefPlannedArrRunway))
            {
                Console.WriteLine($"  ? SimBrief planned arrival runway '{simbriefPlannedArrRunway}' not found in navdata; selecting weather-based runway.");
            }

            arrivalRunwayResult = runwaySelector.SelectArrivalRunway(
                destinationIcao,
                destinationWeather,
                aircraft,
                destinationRunways);
        }

        if (arrivalRunwayResult.SelectedRunway is null)
        {
            Console.WriteLine("✗ Cannot determine arrival runway. Exiting.");
            return;
        }

        Console.WriteLine($"  ✓ Arrival runway {arrivalRunwayResult.SelectedRunway.RunwayIdentifier} selected (wind {destinationWeather.WindDirectionDegrees}°/{destinationWeather.WindSpeedKnots}kt)");

        // Select SID: use SimBrief planned SID if compatible with the chosen runway; otherwise select based on route.
        var availableSids = navDataRepo.GetSids(originIcao);
        Console.WriteLine($"  NavData has {availableSids.Count} SIDs for {originIcao}");
        SidSelectionResult sidResult;
        var selectedDepRunwayId = departureRunwayResult.SelectedRunway.RunwayIdentifier;
        SidSummary? simbriefSid = null;
        if (!string.IsNullOrWhiteSpace(simbriefPlannedSid))
        {
            simbriefSid = availableSids.FirstOrDefault(s =>
                string.Equals(s.ProcedureIdentifier, simbriefPlannedSid, StringComparison.OrdinalIgnoreCase)
                && (string.IsNullOrWhiteSpace(s.RunwayIdentifier)
                    || string.Equals(s.RunwayIdentifier, selectedDepRunwayId, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(s.RunwayIdentifier, "ALL", StringComparison.OrdinalIgnoreCase)));
        }

        if (availableSids.Count == 0 && !string.IsNullOrWhiteSpace(simbriefPlannedSid))
        {
            sidResult = new SidSelectionResult
            {
                Mode = ProcedureSelectionMode.Published,
                SelectedSid = new SidSummary
                {
                    AirportIcao = originIcao,
                    ProcedureIdentifier = simbriefPlannedSid,
                    RunwayIdentifier = selectedDepRunwayId
                },
                MatchingExitFix = null,
                Reason = "Using SimBrief SID (navdata has no SIDs)."
            };
            Console.WriteLine($"  Using SimBrief planned SID {simbriefPlannedSid} (navdata returned zero SIDs).");
        }
        else if (simbriefSid != null)
        {
            sidResult = new SidSelectionResult
            {
                Mode = ProcedureSelectionMode.Published,
                SelectedSid = simbriefSid,
                MatchingExitFix = string.IsNullOrWhiteSpace(simbriefSid.ExitFixIdentifier) ? null : simbriefSid.ExitFixIdentifier,
                Reason = $"Using SimBrief planned SID {simbriefSid.ProcedureIdentifier} for runway {selectedDepRunwayId}."
            };
            Console.WriteLine($"  Using SimBrief planned SID {simbriefSid.ProcedureIdentifier} for runway {selectedDepRunwayId}.");
        }
        else
        {
            if (!string.IsNullOrWhiteSpace(simbriefPlannedSid))
            {
                Console.WriteLine($"  SimBrief planned SID '{simbriefPlannedSid}' not usable with runway {selectedDepRunwayId}; selecting SID based on route.");
            }

            sidResult = procedureSelector.SelectSidForRoute(
                originIcao,
                departureRunwayResult.SelectedRunway,  // AeroAI-selected runway
                enrouteRoute ?? new EnrouteRoute { OriginIcao = originIcao, DestinationIcao = destinationIcao },
                availableSids);
            
            // Print SID selection result
            if (sidResult.Mode == ProcedureSelectionMode.Published && sidResult.SelectedSid != null)
            {
                Console.WriteLine($"  Selected SID {sidResult.SelectedSid.ProcedureIdentifier} via exit fix {sidResult.MatchingExitFix}");
            }
            else
            {
                Console.WriteLine($"  No SID selected: {sidResult.Reason}");
            }
        }

        // Select STAR based on selected runway and enroute waypoints (SimBrief provides enroute fixes)
        // Runway may be from SimBrief if weather-approved, otherwise weather-selected.
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
        // Load optional voice configuration (defaults to off if no API key or opt-out)
        var voiceConfig = VoiceConfigLoader.LoadFromEnvironment();
        var voiceProfiles = VoiceProfileLoader.LoadProfiles();
        var voiceProfileManager = new VoiceProfileManager(voiceConfig, voiceProfiles);
        string? voiceProfileOverride = Environment.GetEnvironmentVariable("AEROAI_VOICE_PROFILE");
        VoiceProfile? selectedProfile = null;
        string controllerRole = "DELIVERY"; // Matches voice profile controller_types
        var sharedHttpClient = new HttpClient(new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
        })
        {
            Timeout = TimeSpan.FromSeconds(60)
        };
        IAtcVoiceEngine voiceEngine = new NullVoiceEngine();
        if (voiceConfig.Enabled && !string.IsNullOrWhiteSpace(voiceConfig.ApiKey))
        {
            if (!string.IsNullOrWhiteSpace(originIcao) && originIcao.Equals("LXGB", StringComparison.OrdinalIgnoreCase) && controllerRole.Equals("CLEARANCE", StringComparison.OrdinalIgnoreCase))
            {
                var gibPref = Environment.GetEnvironmentVariable("AEROAI_VOICE_GIBRALTAR");
                if (!string.IsNullOrWhiteSpace(gibPref) && gibPref.Equals("spanish", StringComparison.OrdinalIgnoreCase))
                {
                    voiceProfileOverride = "gibraltar_spanish";
                }
                else
                {
                    voiceProfileOverride = "gibraltar_english";
                }
            }

            selectedProfile = voiceProfileManager.GetProfileFor(originIcao, controllerRole, voiceProfileOverride);
            Console.WriteLine($"[Voice] Selected profile: {selectedProfile?.DisplayName ?? "fallback"} for {originIcao}/{controllerRole}");
            if (selectedProfile != null)
            {
                // Override config with profile values when present
                voiceConfig = new VoiceConfig
                {
                    Enabled = voiceConfig.Enabled,
                    ApiKey = voiceConfig.ApiKey,
                    ApiBase = voiceConfig.ApiBase,
                    Model = string.IsNullOrWhiteSpace(selectedProfile.TtsModel) ? voiceConfig.Model : selectedProfile.TtsModel!,
                    Voice = string.IsNullOrWhiteSpace(selectedProfile.TtsVoice) ? voiceConfig.Voice : selectedProfile.TtsVoice!,
                    Speed = selectedProfile.SpeakingRate ?? voiceConfig.Speed
                };
                Console.WriteLine($"[TTS] Voice profile: {selectedProfile.DisplayName} ({selectedProfile.Id})");
            }
            try
            {
                voiceEngine = new OpenAiAudioVoiceEngine(voiceConfig, sharedHttpClient);
                Console.WriteLine("[TTS] Using OpenAI TTS");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[TTS disabled: {ex.Message}]");
                voiceEngine = new NullVoiceEngine();
            }
        }

        // Create OpenAI LLM client for ATC responses
        var llm = new AeroAI.Llm.OpenAiLlmClient(
            apiKey: AeroAI.Config.EnvironmentConfig.GetOpenAiApiKey(),
            model: AeroAI.Config.EnvironmentConfig.GetOpenAiModel(),
            baseUrl: AeroAI.Config.EnvironmentConfig.GetOpenAiBaseUrl()
        );

        // Create flight context for LLM session
        var flightContext = new FlightContext
        {
            Callsign = displayCallsign,
            RadioCallsign = callsignDetails?.RadioCallsign ?? displayCallsign,
            RawCallsign = callsignDetails?.Raw ?? displayCallsign,
            AirlineIcao = callsignDetails?.AirlineIcao ?? string.Empty,
            FlightNumber = callsignDetails?.FlightNumber ?? string.Empty,
            AirlineName = callsignDetails?.AirlineName ?? string.Empty,
            AirlineFullName = callsignDetails?.AirlineFullName ?? string.Empty,
            CanonicalCallsign = callsignDetails?.CanonicalCallsign ?? displayCallsign,
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

        Console.WriteLine($"[CALLSIGN] Raw='{flightContext.RawCallsign}', Canonical='{flightContext.CanonicalCallsign}', Radio='{flightContext.Callsign}'");

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
            // Handle radio check requests (including common typos)
            var trimmedInput = pilotInput.Trim().ToLowerInvariant();
            if (trimmedInput.Contains("radio check") || trimmedInput.Contains("raido check") || 
                trimmedInput.Contains("radoi check") || trimmedInput.Contains("radio chek"))
            {
                var checkText = $"{flightContext.Callsign}, radio check, loud and clear.";
                await HandleAtcReplyAsync(checkText, voiceEngine, originIcao, controllerRole, voiceProfileManager, voiceProfileOverride);
                continue;
            }

            try
            {
                // Check if clearance can be auto-issued (data became ready)
                var autoResponse = await atcSession.CheckAndAutoIssueClearanceAsync();
                if (autoResponse is not null)
                {
                    await HandleAtcReplyAsync(autoResponse, voiceEngine, originIcao, controllerRole, voiceProfileManager, voiceProfileOverride);
                    continue;
                }

                var atcResponse = await atcSession.HandlePilotTransmissionAsync(pilotInput);
                if (atcResponse is not null)
                {
                    await HandleAtcReplyAsync(atcResponse, voiceEngine, originIcao, controllerRole, voiceProfileManager, voiceProfileOverride);
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

        sharedHttpClient?.Dispose();
    }

    private static async Task HandleAtcReplyAsync(string replyText, IAtcVoiceEngine voiceEngine, string? departureIcao, string controllerType, VoiceProfileManager manager, string? preferredProfileId)
    {
        Console.WriteLine($"ATC → {replyText}");
        var profile = manager.GetProfileFor(departureIcao, controllerType, preferredProfileId);
        Console.WriteLine($"[Voice] Using: {profile?.DisplayName ?? "fallback"}, style: {profile?.StyleHint?.Substring(0, Math.Min(40, profile?.StyleHint?.Length ?? 0)) ?? "none"}...");
        await voiceEngine.SpeakAsync(replyText, profile);
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

    /// <summary>
    /// Flexible runway identifier matching: handles formats like "24" vs "RW24", "24L" vs "RW24L"
    /// </summary>
    private static bool RunwayIdentifiersMatch(string navDataRwy, string simbriefRwy)
    {
        if (string.IsNullOrWhiteSpace(navDataRwy) || string.IsNullOrWhiteSpace(simbriefRwy))
            return false;

        // Normalize both: strip RW prefix, uppercase
        var normalized1 = navDataRwy.Trim().ToUpperInvariant();
        var normalized2 = simbriefRwy.Trim().ToUpperInvariant();

        if (normalized1.StartsWith("RW"))
            normalized1 = normalized1.Substring(2);
        if (normalized2.StartsWith("RW"))
            normalized2 = normalized2.Substring(2);

        return string.Equals(normalized1, normalized2, StringComparison.OrdinalIgnoreCase);
    }
}
