using System;
using System.Linq;
using System.IO;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AeroAI.Atc;
using AeroAI.Audio;
using AeroAI.Data;
using AeroAI.Models;
using AeroAI.Llm;
using AeroAI.Config;
using AeroAI.UI.Services;
using AtcNavDataDemo.Config;
using AtcNavDataDemo.SimBrief;
using AtcNavDataDemo.Weather;
using AeroAI.Logic;
namespace AeroAI.UI.Services;

public class AtcService : IDisposable
{
    private FlightContext? _flightContext;
    private AeroAiLlmSession? _llmSession;
    private IAtcVoiceEngine? _voiceEngine;
    private VoiceProfileManager? _voiceProfileManager;
    private string? _voiceOverride;
    private readonly HttpClient _httpClient;
    private string? _connectedControllerRole;
    private double? _connectedControllerFrequency;

    public event Action<string, string>? OnAtcMessage;
    public event Action<FlightContext>? OnFlightContextUpdated;
    public event Action<string>? OnDebug;
    public event Action<string>? OnTtsNotice;

    public FlightContext? CurrentFlight => _flightContext;
    public bool IsInitialized => _flightContext != null && _llmSession != null;
    public bool HasPendingConfirmation => _llmSession?.HasPendingConfirmation ?? false;
    public string? ConnectedControllerRole => _connectedControllerRole;
    public double? ConnectedControllerFrequency => _connectedControllerFrequency;

    public AtcService()
    {
        _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
    }

    public async Task InitializeAsync(string simBriefUserId, bool forceRefresh = false)
    {
        EnvironmentConfig.Load();

        var ofp = await LoadFlightPlanWithCacheAsync(simBriefUserId, forceRefresh);

        if (ofp == null)
            throw new Exception("Failed to load SimBrief flight plan");

        var airlineDirectory = AirlineDirectory.Load();
        var callsignDetails = CallsignDetails.FromRaw(ofp.Callsign, airlineDirectory);

        // Weather (CheckWX if available, else defaults)
        var originWeather = await FetchWeatherAsync(ofp.OriginIcao, ofp.OriginMetar);
        var destinationWeather = await FetchWeatherAsync(ofp.DestinationIcao, ofp.DestinationMetar);

        // Persist and derive ATIS letter from METAR changes (deterministic local cache).
        var originAtis = AtisMetarCache.UpdateMetar(ofp.OriginIcao, originWeather.RawMetar);

        // Aircraft performance (default distances + SimBrief ICAO)
        var aircraftPerf = BuildAircraftProfile(ofp.AircraftIcao);

        // Navdata-based departure runway selection (weather aware)
        NavRunwaySummary? selectedDepartureRunway = null;
        var navDataPath = Environment.GetEnvironmentVariable("AEROAI_NAVDATA_PATH")
            ?? @"C:\Users\Craig\AppData\Local\Packages\Microsoft.FlightSimulator_8wekyb3d8bbwe\LocalState\packages\pmdg-aircraft-738\work\NavigationData\e_dfd_PMDG.s3db";

        try
        {
            if (File.Exists(navDataPath))
            {
                var navRepo = new SqliteNavDataRepository(navDataPath);
                var runways = navRepo.GetRunways(ofp.OriginIcao);
                var selector = new RunwaySelector();

                NavRunwaySummary? planned = null;
                if (!string.IsNullOrWhiteSpace(ofp.PlannedDepartureRunway))
                {
                    planned = runways.FirstOrDefault(r => RunwayIdentifiersMatch(r.RunwayIdentifier, ofp.PlannedDepartureRunway));
                }

                if (planned != null && selector.IsRunwayAcceptable(planned, originWeather, aircraftPerf, isDeparture: true))
                {
                    selectedDepartureRunway = planned;
                }
                else if (runways.Count > 0)
                {
                    selectedDepartureRunway = selector.SelectDepartureRunway(ofp.OriginIcao, originWeather, aircraftPerf, runways).SelectedRunway;
                }

                // Fallback: if still null, use first runway from navdata
                if (selectedDepartureRunway == null && runways.Count > 0)
                    selectedDepartureRunway = runways.FirstOrDefault();
            }
        }
        catch
        {
            selectedDepartureRunway = null;
        }

        _flightContext = new FlightContext
        {
            Callsign = callsignDetails.RadioCallsign,
            RawCallsign = callsignDetails.Raw,
            AirlineIcao = callsignDetails.AirlineIcao ?? string.Empty,
            FlightNumber = callsignDetails.FlightNumber ?? string.Empty,
            AirlineName = callsignDetails.AirlineName ?? string.Empty,
            AirlineFullName = callsignDetails.AirlineFullName ?? string.Empty,
            CanonicalCallsign = callsignDetails.CanonicalCallsign,
            OriginIcao = ofp.OriginIcao,
            OriginName = ofp.OriginName,
            DestinationIcao = ofp.DestinationIcao,
            DestinationName = ofp.DestinationName,
            OriginWeather = originWeather,
            DestinationWeather = destinationWeather,
            DepartureAtisLetter = originAtis.AtisLetter,
            EnrouteRoute = new EnrouteRoute
            {
                OriginIcao = ofp.OriginIcao,
                DestinationIcao = ofp.DestinationIcao,
                WaypointIdentifiers = ofp.WaypointIdentifiers
            },
            CurrentPhase = FlightPhase.Preflight_Clearance,
            CurrentAtcUnit = AtcUnit.ClearanceDelivery,
            CruiseFlightLevel = ofp.CruiseFlightLevel
        };

        if (_flightContext.DepartureRunway == null && selectedDepartureRunway != null)
        {
            _flightContext.DepartureRunway = selectedDepartureRunway;
        }

        if (_flightContext.DepartureRunway == null && !string.IsNullOrWhiteSpace(ofp.PlannedDepartureRunway))
        {
            _flightContext.DepartureRunway = new NavRunwaySummary
            {
                AirportIcao = ofp.OriginIcao,
                RunwayIdentifier = ofp.PlannedDepartureRunway
            };
        }
        // Absolute fallback: choose first navdata runway if nothing else set
        if (_flightContext.DepartureRunway == null && selectedDepartureRunway == null)
        {
            try
            {
                var navDataPathFallback = Environment.GetEnvironmentVariable("AEROAI_NAVDATA_PATH")
                    ?? @"C:\Users\Craig\AppData\Local\Packages\Microsoft.FlightSimulator_8wekyb3d8bbwe\LocalState\packages\pmdg-aircraft-738\work\NavigationData\e_dfd_PMDG.s3db";
                if (File.Exists(navDataPathFallback))
                {
                    var navRepo = new SqliteNavDataRepository(navDataPathFallback);
                    var runways = navRepo.GetRunways(ofp.OriginIcao);
                    if (runways.Count > 0)
                        _flightContext.DepartureRunway = runways.First();
                }
            }
            catch
            {
                // ignore
            }
        }

        if (!string.IsNullOrWhiteSpace(ofp.PlannedSid))
        {
            _flightContext.SelectedSid = new SidSelectionResult
            {
                Mode = ProcedureSelectionMode.Published,
                SelectedSid = new SidSummary
                {
                    AirportIcao = ofp.OriginIcao,
                    ProcedureIdentifier = ofp.PlannedSid
                }
            };
        }

        // Populate aircraft performance/profile
        _flightContext.Aircraft = aircraftPerf;

        // Resolve which unit should handle clearance (Delivery → Ground → Tower → Approach → Center → UNICOM).
        var routing = ClearanceUnitResolver.ResolveForClearance(_flightContext.OriginIcao);
        _flightContext.CurrentAtcUnit = routing.Unit;
        _flightContext.NoAtcAvailable = !routing.HasAtc;
        _flightContext.CurrentFrequency = routing.FrequencyMhz?.ToString("F3");

        var voiceConfig = VoiceConfigLoader.LoadFromEnvironment();
        var userConfig = UserConfigStore.Load();
        var voiceProfiles = VoiceProfileLoader.LoadProfiles();
        _voiceProfileManager = new VoiceProfileManager(voiceConfig, voiceProfiles);
        _voiceEngine = await CreateVoiceEngineAsync(voiceConfig, userConfig);
        _voiceOverride = Environment.GetEnvironmentVariable("AEROAI_VOICE_PROFILE");

        var llmClient = new OpenAiLlmClient(
            apiKey: EnvironmentConfig.GetOpenAiApiKey(),
            model: EnvironmentConfig.GetOpenAiModel(),
            baseUrl: EnvironmentConfig.GetOpenAiBaseUrl());

        _llmSession = new AeroAiLlmSession(llmClient, _flightContext);

        // Initialize the shared normalizer with STT correction layer
        var sttCorrectionLayer = new SttCorrectionLayer(OnDebug);
        PilotTransmissionNormalizer.Initialize(sttCorrectionLayer);

        OnFlightContextUpdated?.Invoke(_flightContext);
    }

    private async Task<IAtcVoiceEngine> CreateVoiceEngineAsync(VoiceConfig voiceConfig, UserConfig userConfig)
    {
        var voiceLabEnabled = userConfig.Tts?.VoiceLabEnabled ?? true;
        var voiceLabClient = new VoiceLabTtsClient(_httpClient, userConfig);
        var openAiFallback = voiceConfig.Enabled && !string.IsNullOrWhiteSpace(voiceConfig.ApiKey)
            ? new OpenAiAudioVoiceEngine(voiceConfig, _httpClient)
            : null;
        var voiceLab = new VoiceLabAudioVoiceEngine(
            voiceLabClient,
            () => _flightContext,
            () => _connectedControllerRole,
            openAiFallback,
            msg => OnTtsNotice?.Invoke(msg));
        bool voiceLabReady = false;
        if (voiceLabEnabled)
        {
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                voiceLabReady = (await voiceLabClient.HealthAsync(cts.Token)).Online;
            }
            catch
            {
                voiceLabReady = false;
            }
        }

        if (voiceLabReady)
        {
            OnDebug?.Invoke("VoiceLab TTS connected (primary).");
            return voiceLab;
        }

        if (!voiceLabEnabled)
        {
            OnDebug?.Invoke("VoiceLab disabled in settings.");
        }

        if (openAiFallback != null)
        {
            OnDebug?.Invoke("VoiceLab unavailable. Falling back to OpenAI TTS.");
            return openAiFallback;
        }

        OnDebug?.Invoke("TTS disabled (VoiceLab unavailable and no OpenAI API key).");
        return new NullVoiceEngine();
    }

    public void UpdateConnectedController(string? role, double? frequencyMhz)
    {
        if (string.IsNullOrWhiteSpace(role) || frequencyMhz == null || frequencyMhz <= 0)
        {
            _connectedControllerRole = null;
            _connectedControllerFrequency = null;
            return;
        }

        _connectedControllerRole = role.Trim().ToLowerInvariant();
        _connectedControllerFrequency = frequencyMhz;
    }

    private static AircraftPerformanceProfile BuildAircraftProfile(string? icaoType)
    {
        return new AircraftPerformanceProfile
        {
            IcaoType = string.IsNullOrWhiteSpace(icaoType) ? "B738" : icaoType,
            RequiredTakeoffDistanceFeet = 8000,
            RequiredLandingDistanceFeet = 6000,
            MaxTailwindComponentKnots = 10,
            MaxCrosswindComponentKnots = 25
        };
    }

    private async Task<WeatherInfo> FetchWeatherAsync(string icao, string? simbriefMetarFallback = null)
    {
        var cached = AtisMetarCache.Get(icao);
        var defaultWx = new WeatherInfo
        {
            AirportIcao = icao,
            RawMetar = cached.RawMetar,
            WindDirectionDegrees = 270,
            WindSpeedKnots = 10,
            VisibilityMeters = 10000,
            CeilingFeet = 5000,
            IsIfr = false,
            IsLowVisibility = false
        };

        try
        {
            var rawMetar = await FetchRawMetarFromAviationWeatherAsync(icao);
            if (string.IsNullOrWhiteSpace(rawMetar))
                rawMetar = simbriefMetarFallback;

            if (!string.IsNullOrWhiteSpace(rawMetar))
            {
                var parsed = ParseMetar(rawMetar, defaultWx);
                AtisMetarCache.UpdateMetar(icao, parsed.RawMetar);
                return parsed;
            }
        }
        catch (Exception ex)
        {
            OnDebug?.Invoke($"METAR fetch failed for {icao}: {ex.Message}");
        }

        return defaultWx;
    }

    private async Task<string?> FetchRawMetarFromAviationWeatherAsync(string icao)
    {
        if (string.IsNullOrWhiteSpace(icao))
            return null;

        try
        {
            var url = $"https://aviationweather.gov/api/data/metar?ids={icao.ToUpperInvariant()}&hours=0&sep=true";
            using var response = await _httpClient.GetAsync(url);
            if (!response.IsSuccessStatusCode)
            {
                OnDebug?.Invoke($"AviationWeather METAR fetch failed for {icao}: {response.StatusCode}");
                return null;
            }

            var content = await response.Content.ReadAsStringAsync();
            if (string.IsNullOrWhiteSpace(content))
                return null;

            // Response is plain-text METAR(s), one per line.
            var firstLine = content.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
            return string.IsNullOrWhiteSpace(firstLine) ? null : firstLine.Trim();
        }
        catch (Exception ex)
        {
            OnDebug?.Invoke($"AviationWeather METAR fetch error for {icao}: {ex.Message}");
            return null;
        }
    }

    private static WeatherInfo ParseMetar(string rawMetar, WeatherInfo fallback)
    {
        if (string.IsNullOrWhiteSpace(rawMetar))
            return fallback;

        int windDir = fallback.WindDirectionDegrees;
        int windSpeed = fallback.WindSpeedKnots;
        int visibility = fallback.VisibilityMeters;
        int ceiling = fallback.CeilingFeet;

        var tokens = rawMetar.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var windToken = tokens.FirstOrDefault(t => t.EndsWith("KT", StringComparison.OrdinalIgnoreCase) && (t.Length >= 5 && t.Length <= 8));
        if (!string.IsNullOrWhiteSpace(windToken))
        {
            var m = Regex.Match(windToken, @"^(?<dir>\d{3}|VRB)(?<spd>\d{2,3})(?:G\d{2,3})?KT$", RegexOptions.IgnoreCase);
            if (m.Success)
            {
                var dirStr = m.Groups["dir"].Value;
                if (dirStr != "VRB" && int.TryParse(dirStr, out var dirVal))
                    windDir = dirVal;

                if (int.TryParse(m.Groups["spd"].Value, out var spdVal))
                    windSpeed = spdVal;
            }
        }

        var visToken = tokens.FirstOrDefault(t => Regex.IsMatch(t, @"^\d{4}$"));
        if (!string.IsNullOrWhiteSpace(visToken) && int.TryParse(visToken, out var visVal))
            visibility = visVal;

        var cloudHeights = tokens
            .Where(t => t.Length >= 5 && Regex.IsMatch(t, @"^(FEW|SCT|BKN|OVC)\d{3}", RegexOptions.IgnoreCase))
            .Select(t => int.TryParse(t.Substring(3, 3), out var h) ? h * 100 : int.MaxValue)
            .Where(h => h > 0 && h < int.MaxValue)
            .ToList();
        if (cloudHeights.Count > 0)
            ceiling = cloudHeights.Min();

        var isIfr = visibility < 4800 || (ceiling > 0 && ceiling < 1000);
        var isLowVis = visibility < 800 || (ceiling > 0 && ceiling < 200);

        return new WeatherInfo
        {
            AirportIcao = fallback.AirportIcao,
            RawMetar = rawMetar.Trim(),
            WindDirectionDegrees = windDir,
            WindSpeedKnots = windSpeed,
            VisibilityMeters = visibility,
            CeilingFeet = ceiling,
            IsIfr = isIfr,
            IsLowVisibility = isLowVis
        };
    }

    private static (string? ApiKey, string? Source) ResolveCheckWxApiKey()
    {
        try
        {
            foreach (var path in ResolveCandidatePaths("checkwx.json"))
            {
                if (!File.Exists(path))
                    continue;

                var json = File.ReadAllText(path);
                var elem = JsonSerializer.Deserialize<JsonElement>(json);
                if (elem.TryGetProperty("ApiKey", out var key))
                {
                    var value = key.GetString();
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        return (value, path);
                    }
                }
            }
        }
        catch
        {
            // ignore
        }

        return (null, null);
    }

    private static IEnumerable<string> ResolveCandidatePaths(string fileName)
    {
        yield return Path.Combine(Directory.GetCurrentDirectory(), fileName);

        // Walk up from the executable directory so dev/publish layouts can still find repo-root configs.
        // Bounded to avoid unbounded filesystem walks.
        List<string>? parentPaths = null;
        try
        {
            parentPaths = new List<string>(capacity: 12);
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            for (var depth = 0; dir != null && depth < 12; depth++)
            {
                parentPaths.Add(Path.Combine(dir.FullName, fileName));
                dir = dir.Parent;
            }
        }
        catch
        {
            parentPaths = null;
        }

        if (parentPaths != null)
        {
            foreach (var path in parentPaths)
                yield return path;
        }
    }

    private static bool RunwayIdentifiersMatch(string navDataRwy, string simbriefRwy)
    {
        if (string.IsNullOrWhiteSpace(navDataRwy) || string.IsNullOrWhiteSpace(simbriefRwy))
            return false;

        var normalized1 = navDataRwy.Trim().ToUpperInvariant();
        var normalized2 = simbriefRwy.Trim().ToUpperInvariant();

        if (normalized1.StartsWith("RW"))
            normalized1 = normalized1.Substring(2);
        if (normalized2.StartsWith("RW"))
            normalized2 = normalized2.Substring(2);

        return string.Equals(normalized1, normalized2, StringComparison.OrdinalIgnoreCase);
    }

    public async Task<string?> SendPilotMessageAsync(string message)
    {
        if (_llmSession == null || _flightContext == null)
        {
            OnDebug?.Invoke("[ATC] Session or flight context not initialized");
            return GetFallbackResponse("say again");
        }

        string? response;
        try
        {
            response = await _llmSession.HandlePilotTransmissionAsync(message);
        }
        catch (Exception ex)
        {
            OnDebug?.Invoke($"[ATC ERROR] HandlePilotTransmissionAsync failed: {ex.Message}\n{ex.StackTrace}");
            response = GetFallbackResponse("say again");
        }

        // HARD RULE: Never return null/empty - always provide a response
        if (string.IsNullOrWhiteSpace(response))
        {
            OnDebug?.Invoke("[ATC] Response was null/empty - returning fallback");
            response = GetFallbackResponse(null);
        }

        if (!string.IsNullOrWhiteSpace(response))
        {
            OnAtcMessage?.Invoke(GetCurrentStation(), response);
            if (_flightContext != null)
                OnFlightContextUpdated?.Invoke(_flightContext);

            // Read it out after showing it in the UI
            if (_voiceEngine != null && _voiceProfileManager != null)
            {
                var controllerRole = _flightContext.CurrentAtcUnit.ToString().ToUpperInvariant();
                var profile = _voiceProfileManager.GetProfileFor(
                    _flightContext.OriginIcao, controllerRole, _voiceOverride);
                OnDebug?.Invoke($"Voice profile: {profile?.Id ?? "null"} ({profile?.DisplayName ?? "none"}), controller={controllerRole}, origin={_flightContext.OriginIcao}, override={_voiceOverride ?? "none"}, voice={profile?.TtsVoice ?? "default"}, model={profile?.TtsModel ?? "default"}, styleHint={(profile?.StyleHint ?? "none")}");
                
                var speakText = NormalizeForSpeech(response);
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await _voiceEngine.SpeakAsync(speakText, profile);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[TTS error: {ex.Message}]");
                    }
                });
            }
        }

        return response;
    }

    /// <summary>
    /// Generate a fallback ATC response when normal processing fails.
    /// </summary>
    private string GetFallbackResponse(string? hint)
    {
        var cs = _flightContext?.RadioCallsign ?? _flightContext?.Callsign ?? "Aircraft";
        
        // Check what slot is being collected for a more specific response
        if (_llmSession != null)
        {
            // Use generic fallback based on hint
            if (!string.IsNullOrWhiteSpace(hint))
            {
                return $"{cs}, {hint}.";
            }
        }
        
        return $"{cs}, say again.";
    }

    public Task SpeakAtcAsync(string text, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(text) || _voiceEngine == null || _voiceProfileManager == null || _flightContext == null)
            return Task.CompletedTask;

        var controllerRole = _flightContext.CurrentAtcUnit.ToString().ToUpperInvariant();
        var profile = _voiceProfileManager.GetProfileFor(
            _flightContext.OriginIcao, controllerRole, _voiceOverride);
        OnDebug?.Invoke($"Voice profile (manual): {profile?.Id ?? "null"} ({profile?.DisplayName ?? "none"}), controller={controllerRole}, origin={_flightContext.OriginIcao}, override={_voiceOverride ?? "none"}");

        var speakText = NormalizeForSpeech(text);
        return _voiceEngine.SpeakAsync(speakText, profile, cancellationToken);
    }

    public string GetCurrentStation()
    {
        var airport = _flightContext?.OriginIcao ?? "XXXX";
        var unit = _flightContext?.CurrentAtcUnit ?? AtcUnit.ClearanceDelivery;
        var freq = ClearanceUnitResolver.ResolveFrequency(airport, unit) ?? 122.800;

        var unitText = unit switch
        {
            AtcUnit.Ground => "Ground",
            AtcUnit.Tower => "Tower",
            AtcUnit.Departure => "Departure",
            AtcUnit.Center => "Center",
            AtcUnit.Arrival => "Arrival",
            AtcUnit.Approach => "Approach",
            _ => "Delivery"
        };
        if (_flightContext?.NoAtcAvailable == true)
        {
            unitText = "UNICOM";
            freq = 122.800;
        }

        return $"{freq:F3} : {airport} {unitText}";
    }

    public void ResetFlight()
    {
        _flightContext?.ResetForNewFlight();
        _llmSession?.ResetForNewFlight();
    }

    public void Dispose()
    {
        _httpClient?.Dispose();
    }

    private static string NormalizeForSpeech(string text)
    {
        // Expand FL240 -> "flight level two four zero" for better TTS pronunciation.
        return Regex.Replace(text, @"\bFL(\d{2,3})\b", match =>
        {
            var digits = match.Groups[1].Value;
            var spoken = string.Join(" ", digits.Select(DigitToWord));
            return $"flight level {spoken}";
        }, RegexOptions.IgnoreCase);
    }

    private static string DigitToWord(char c) => c switch
    {
        '0' => "zero",
        '1' => "one",
        '2' => "two",
        '3' => "three",
        '4' => "four",
        '5' => "five",
        '6' => "six",
        '7' => "seven",
        '8' => "eight",
        '9' => "nine",
        _ => c.ToString()
    };

    private async Task<FlightPlan?> LoadFlightPlanWithCacheAsync(string pilotId, bool forceRefresh = false)
    {
        var cachePath = GetSimbriefCachePath(pilotId);

        // If not forcing refresh, prefer cache first.
        if (!forceRefresh)
        {
            var cached = TryReadSimbriefCache(cachePath);
            if (cached != null)
            {
                OnDebug?.Invoke($"SimBrief: loaded from cache '{cachePath}'.");
                return cached;
            }
        }

        // Fetch from SimBrief, then cache.
        try
        {
            var client = new SimBriefClient(_httpClient);
            var ofp = await client.FetchLatestFlightPlanAsync(pilotId);
            if (ofp != null)
            {
                TryWriteSimbriefCache(cachePath, ofp);
                OnDebug?.Invoke($"SimBrief: fetched fresh and cached to '{cachePath}'.");
            }
            return ofp;
        }
        catch (Exception ex)
        {
            OnDebug?.Invoke($"SimBrief fetch failed: {ex.Message}. Trying cache fallback.");
            var cached = TryReadSimbriefCache(cachePath);
            if (cached != null)
            {
                OnDebug?.Invoke($"SimBrief: using cached plan '{cachePath}' after fetch failure.");
            }
            return cached;
        }
    }

    private string GetSimbriefCachePath(string pilotId)
    {
        string safeId = string.IsNullOrWhiteSpace(pilotId)
            ? "unknown"
            : Regex.Replace(pilotId, "[^A-Za-z0-9_-]", "_");
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var dir = Path.Combine(appData, "AeroAI", "cache");
        try { Directory.CreateDirectory(dir); } catch { /* ignore */ }
        return Path.Combine(dir, $"simbrief_{safeId}.json");
    }

    private FlightPlan? TryReadSimbriefCache(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<FlightPlan>(json);
        }
        catch
        {
            return null;
        }
    }

    private void TryWriteSimbriefCache(string path, FlightPlan plan)
    {
        try
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }
            var json = JsonSerializer.Serialize(plan, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(path, json);
        }
        catch
        {
            // swallow cache write failures
        }
    }
}
