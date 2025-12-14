using System;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using AeroAI.Atc;
using AeroAI.Audio;
using AeroAI.Data;
using AeroAI.Models;
using AeroAI.Llm;
using AeroAI.Config;
using AtcNavDataDemo.Config;
using AtcNavDataDemo.SimBrief;

namespace AeroAI.UI.Services;

public class AtcService : IDisposable
{
    private FlightContext? _flightContext;
    private AeroAiLlmSession? _llmSession;
    private IAtcVoiceEngine? _voiceEngine;
    private VoiceProfileManager? _voiceProfileManager;
    private readonly HttpClient _httpClient;

    public event Action<string, string>? OnAtcMessage;
    public event Action<FlightContext>? OnFlightContextUpdated;

    public FlightContext? CurrentFlight => _flightContext;
    public bool IsInitialized => _flightContext != null && _llmSession != null;

    public AtcService()
    {
        _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
    }

    public async Task InitializeAsync(string simBriefUserId)
    {
        EnvironmentConfig.Load();

        var simBriefClient = new SimBriefClient(_httpClient);
        var ofp = await simBriefClient.FetchLatestFlightPlanAsync(simBriefUserId);

        if (ofp == null)
            throw new Exception("Failed to load SimBrief flight plan");

        var airlineDirectory = AirlineDirectory.Load();
        var callsignDetails = CallsignDetails.FromRaw(ofp.Callsign, airlineDirectory);

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
            DestinationIcao = ofp.DestinationIcao,
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

        if (!string.IsNullOrWhiteSpace(ofp.PlannedDepartureRunway))
        {
            _flightContext.DepartureRunway = new NavRunwaySummary
            {
                AirportIcao = ofp.OriginIcao,
                RunwayIdentifier = ofp.PlannedDepartureRunway
            };
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

        var voiceConfig = VoiceConfigLoader.LoadFromEnvironment();
        var voiceProfiles = VoiceProfileLoader.LoadProfiles();
        _voiceProfileManager = new VoiceProfileManager(voiceConfig, voiceProfiles);
        _voiceEngine = voiceConfig.Enabled && !string.IsNullOrWhiteSpace(voiceConfig.ApiKey)
            ? new OpenAiAudioVoiceEngine(voiceConfig, _httpClient)
            : new NullVoiceEngine();

        var llmClient = new OpenAiLlmClient(
            apiKey: EnvironmentConfig.GetOpenAiApiKey(),
            model: EnvironmentConfig.GetOpenAiModel(),
            baseUrl: EnvironmentConfig.GetOpenAiBaseUrl());

        _llmSession = new AeroAiLlmSession(llmClient, _flightContext);

        OnFlightContextUpdated?.Invoke(_flightContext);
    }

    public async Task<string?> SendPilotMessageAsync(string message)
    {
        if (_llmSession == null || _flightContext == null)
            return null;

        var response = await _llmSession.HandlePilotTransmissionAsync(message);

        if (!string.IsNullOrWhiteSpace(response))
        {
            OnAtcMessage?.Invoke(GetCurrentStation(), response);

            // Read it out after showing it in the UI
            if (_voiceEngine != null && _voiceProfileManager != null)
            {
                var controllerRole = _flightContext.CurrentAtcUnit.ToString().ToUpperInvariant();
                var profile = _voiceProfileManager.GetProfileFor(
                    _flightContext.OriginIcao, controllerRole, null);
                
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

    public string GetCurrentStation()
    {
        var airport = _flightContext?.OriginIcao ?? "XXXX";
        string freq = "121.800";
        if (AirportFrequencies.TryGetFrequencies(airport, out var freqs) && freqs.Ground is double gndFreq)
            freq = gndFreq.ToString("F3");
        var unit = _flightContext?.CurrentAtcUnit switch
        {
            AtcUnit.Ground => "Ground",
            AtcUnit.Tower => "Tower",
            AtcUnit.Departure => "Departure",
            AtcUnit.Center => "Center",
            AtcUnit.Arrival => "Arrival",
            AtcUnit.Approach => "Approach",
            _ => "Delivery"
        };
        return $"{freq} : {airport} {unit}";
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
}
