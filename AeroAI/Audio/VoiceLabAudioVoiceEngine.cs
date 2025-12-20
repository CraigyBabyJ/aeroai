using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AeroAI.Atc;
using AeroAI.Data;
using AtcNavDataDemo.Config;

namespace AeroAI.Audio;

/// <summary>
/// Voice engine that calls the local VoiceLab FastAPI service (/tts) and plays WAV output.
/// </summary>
public sealed class VoiceLabAudioVoiceEngine : IAtcVoiceEngine
{
    private readonly ITtsClient _client;
    private readonly Func<FlightContext?> _getFlightContext;
    private readonly Func<string?>? _getConnectedRole;
    private readonly IAtcVoiceEngine? _fallback;
    private readonly Action<string>? _onStatus;

    public VoiceLabAudioVoiceEngine(
        ITtsClient client,
        Func<FlightContext?> getFlightContext,
        Func<string?>? getConnectedRole = null,
        IAtcVoiceEngine? fallback = null,
        Action<string>? onStatus = null)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _getFlightContext = getFlightContext ?? throw new ArgumentNullException(nameof(getFlightContext));
        _getConnectedRole = getConnectedRole;
        _fallback = fallback;
        _onStatus = onStatus;
    }

    public async Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default)
    {
        var health = await _client.HealthAsync(cancellationToken);
        return health.Online;
    }

    public async Task SpeakAsync(string text, VoiceProfile? profile = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(text))
            return;

        var flight = _getFlightContext();
        var connectedRole = NormalizeRole(_getConnectedRole?.Invoke());
        var role = connectedRole ?? ResolveRole(profile);
        var speed = profile?.SpeakingRate ?? 1.0;
        var voiceId = string.IsNullOrWhiteSpace(profile?.TtsVoice) ? "auto" : profile!.TtsVoice!;

        var hints = AirportDataService.GetRegionHints(flight?.OriginIcao);

        var request = new TtsRequest
        {
            Text = text,
            VoiceId = voiceId,
            Role = role,
            Language = "en",
            Speed = speed,
            Format = "wav",
            AirportIcao = flight?.OriginIcao,
            RegionPrefix = hints.RegionPrefix,
            IsoCountry = hints.IsoCountry,
            IsoRegion = hints.IsoRegion
        };

        try
        {
            var result = await _client.SynthesizeAsync(request, cancellationToken);
            var unit = MapControllerTypeToUnit(profile);
            var audioBytes = RadioEffectProcessor.ApplyToWavResponse(result.WavBytes, unit);
            await TtsPlayback.PlayWavBytesAsync(audioBytes, cancellationToken);
        }
        catch (Exception ex)
        {
            if (_fallback != null)
            {
                _onStatus?.Invoke("VoiceLab unavailable. Falling back to OpenAI TTS.");
                await _fallback.SpeakAsync(text, profile, cancellationToken);
                return;
            }

            _onStatus?.Invoke("VoiceLab unavailable. Audio skipped.");
            Console.WriteLine($"[VoiceLab] TTS error: {ex.Message}");
        }
    }

    private static string? ResolveRole(VoiceProfile? profile)
    {
        var role = profile?.ControllerTypes?.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(role))
            return null;
        return role.Trim().ToLowerInvariant();
    }

    private static string? NormalizeRole(string? role)
    {
        if (string.IsNullOrWhiteSpace(role))
            return null;
        return role.Trim().ToLowerInvariant();
    }

    private static AtcUnit MapControllerTypeToUnit(VoiceProfile? profile)
    {
        if (profile?.ControllerTypes == null || profile.ControllerTypes.Count == 0)
            return AtcUnit.ClearanceDelivery;

        var controllerType = profile.ControllerTypes.FirstOrDefault()?.ToUpperInvariant() ?? "";
        return controllerType switch
        {
            "DELIVERY" => AtcUnit.ClearanceDelivery,
            "GROUND" => AtcUnit.Ground,
            "TOWER" => AtcUnit.Tower,
            "DEPARTURE" => AtcUnit.Departure,
            "CENTER" => AtcUnit.Center,
            "ARRIVAL" => AtcUnit.Arrival,
            "APPROACH" => AtcUnit.Approach,
            _ => AtcUnit.ClearanceDelivery
        };
    }
}
