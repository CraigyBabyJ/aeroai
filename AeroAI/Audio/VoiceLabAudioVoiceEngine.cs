using System;
using System.Threading;
using System.Threading.Tasks;
using AeroAI.Atc;
using AeroAI.Data;

namespace AeroAI.Audio;

/// <summary>
/// Voice engine that calls the local VoiceLab FastAPI service (/tts) and plays WAV output.
/// </summary>
public sealed class VoiceLabAudioVoiceEngine : IAtcVoiceEngine
{
    private readonly ITtsClient _client;
    private readonly Func<FlightContext?> _getFlightContext;
    private readonly Action<string>? _onStatus;
    private readonly Action<string>? _onDebug;
    private readonly Func<byte[], CancellationToken, Task> _playWavAsync;

    public VoiceLabAudioVoiceEngine(
        ITtsClient client,
        Func<FlightContext?> getFlightContext,
        Action<string>? onStatus = null,
        Action<string>? onDebug = null,
        Func<byte[], CancellationToken, Task>? playWavAsync = null)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _getFlightContext = getFlightContext ?? throw new ArgumentNullException(nameof(getFlightContext));
        _onStatus = onStatus;
        _onDebug = onDebug;
        _playWavAsync = playWavAsync ?? TtsPlayback.PlayWavBytesAsync;
    }

    public async Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default)
    {
        var health = await _client.HealthAsync(cancellationToken);
        return health.Online;
    }

    public async Task SpeakAsync(string text, string? role = null, string? facilityIcao = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(text))
            return;

        var flight = _getFlightContext();
        var resolvedRole = NormalizeRole(role) ?? ResolveRoleFromUnit(flight?.CurrentAtcUnit);
        var resolvedFacility = ResolveFacilityIcao(facilityIcao, flight);
        var sessionId = ResolveSessionId(flight, resolvedFacility);

        var hints = AirportDataService.GetRegionHints(resolvedFacility);

        var request = new TtsRequest
        {
            Text = text,
            VoiceId = "auto",
            Role = resolvedRole,
            SessionId = sessionId,
            Language = "en",
            Speed = 1.0,
            Format = "wav",
            AirportIcao = resolvedFacility,
            RegionPrefix = hints.RegionPrefix,
            IsoCountry = hints.IsoCountry,
            IsoRegion = hints.IsoRegion
        };

        try
        {
            var result = await _client.SynthesizeAsync(request, cancellationToken);
            var unit = MapRoleToUnit(resolvedRole, flight?.CurrentAtcUnit);
            var audioBytes = RadioEffectProcessor.ApplyToWavResponse(result.WavBytes, unit);
            await _playWavAsync(audioBytes, cancellationToken);
        }
        catch (Exception ex)
        {
            _onDebug?.Invoke($"[VoiceLab] error: {ex.GetType().Name}: {ex.Message}");
            _onStatus?.Invoke("VoiceLab unavailable. Audio skipped.");
            Console.WriteLine($"[VoiceLab] TTS error: {ex.Message}");
        }
    }

    private static string? NormalizeRole(string? role)
    {
        if (string.IsNullOrWhiteSpace(role))
            return null;
        return role.Trim().ToLowerInvariant();
    }

    private static string? ResolveRoleFromUnit(AtcUnit? unit)
    {
        if (unit == null)
            return null;
        return unit.Value switch
        {
            AtcUnit.ClearanceDelivery => "delivery",
            AtcUnit.Ground => "ground",
            AtcUnit.Tower => "tower",
            AtcUnit.Departure => "departure",
            AtcUnit.Center => "center",
            AtcUnit.Arrival => "approach",
            AtcUnit.Approach => "approach",
            _ => "delivery"
        };
    }

    private static string? ResolveFacilityIcao(string? facilityIcao, FlightContext? flight)
    {
        if (!string.IsNullOrWhiteSpace(facilityIcao))
            return facilityIcao.Trim().ToUpperInvariant();
        if (flight == null)
            return null;

        bool useDestination = flight.CurrentPhase is FlightPhase.Descent_Arrival
            or FlightPhase.Approach
            or FlightPhase.Landing
            or FlightPhase.Taxi_In
            or FlightPhase.Complete;

        var icao = useDestination ? flight.DestinationIcao : flight.OriginIcao;
        if (string.IsNullOrWhiteSpace(icao))
            icao = useDestination ? flight.OriginIcao : flight.DestinationIcao;
        return string.IsNullOrWhiteSpace(icao) ? null : icao.Trim().ToUpperInvariant();
    }

    private static string? ResolveSessionId(FlightContext? flight, string? facilityIcao)
    {
        if (flight != null)
        {
            if (!string.IsNullOrWhiteSpace(flight.RadioCallsign))
                return flight.RadioCallsign.Trim();
            if (!string.IsNullOrWhiteSpace(flight.Callsign))
                return flight.Callsign.Trim();
        }
        return string.IsNullOrWhiteSpace(facilityIcao) ? null : facilityIcao.Trim().ToUpperInvariant();
    }

    private static AtcUnit MapRoleToUnit(string? role, AtcUnit? fallbackUnit)
    {
        if (!string.IsNullOrWhiteSpace(role))
        {
            return role.Trim().ToLowerInvariant() switch
            {
                "delivery" => AtcUnit.ClearanceDelivery,
                "ground" => AtcUnit.Ground,
                "tower" => AtcUnit.Tower,
                "departure" => AtcUnit.Departure,
                "center" => AtcUnit.Center,
                "arrival" => AtcUnit.Arrival,
                "approach" => AtcUnit.Approach,
                _ => AtcUnit.ClearanceDelivery
            };
        }

        return fallbackUnit ?? AtcUnit.ClearanceDelivery;
    }
}
