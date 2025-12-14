using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AeroAI.Atc;
using AtcNavDataDemo.Config;

namespace AeroAI.Audio;

/// <summary>
/// Voice engine that calls OpenAI /audio/speech (gpt-4o-mini-tts) and plays WAV output with squelch tail.
/// </summary>
public sealed class OpenAiAudioVoiceEngine : IAtcVoiceEngine
{
    private readonly VoiceConfig _config;
    private readonly HttpClient _http;

    public OpenAiAudioVoiceEngine(VoiceConfig config, HttpClient http)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _http = http ?? throw new ArgumentNullException(nameof(http));
    }

    public async Task SpeakAsync(string text, AtcNavDataDemo.Config.VoiceProfile? profile = null, CancellationToken cancellationToken = default)
    {
        if (!_config.Enabled) return;
        if (string.IsNullOrWhiteSpace(text)) return;
        if (string.IsNullOrWhiteSpace(_config.ApiKey)) return;

        try
        {
            var effectiveModel = profile?.TtsModel ?? _config.Model;
            var effectiveVoice = profile?.TtsVoice ?? _config.Voice;
            var effectiveSpeed = profile?.SpeakingRate ?? _config.Speed;
            var effectiveInstructions = profile?.StyleHint ?? "";

            var requestUri = $"{_config.ApiBase.TrimEnd('/')}/audio/speech";
            
            // Build request body - include instructions for gpt-4o-mini-tts
            object body;
            if (!string.IsNullOrWhiteSpace(effectiveInstructions) && effectiveModel.Contains("gpt-4o"))
            {
                body = new
                {
                    model = effectiveModel,
                    voice = effectiveVoice,
                    input = text,
                    instructions = effectiveInstructions,
                    response_format = "wav",
                    speed = effectiveSpeed
                };
            }
            else
            {
                body = new
                {
                    model = effectiveModel,
                    voice = effectiveVoice,
                    input = text,
                    response_format = "wav",
                    speed = effectiveSpeed
                };
            }

            using var request = new HttpRequestMessage(HttpMethod.Post, requestUri);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _config.ApiKey);
            request.Headers.Accept.Clear();
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("audio/wav"));
            request.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();

            var audioBytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
            
            // Debug: Check what format OpenAI actually sent
            var header = audioBytes.Length >= 4 
                ? $"{(char)audioBytes[0]}{(char)audioBytes[1]}{(char)audioBytes[2]}{(char)audioBytes[3]}" 
                : "???";
            Console.WriteLine($"[TTS Debug] Received {audioBytes.Length} bytes, header: {header}");
            
            // Apply squelch tail based on controller type
            var unit = MapControllerTypeToUnit(profile);
            audioBytes = RadioEffectProcessor.ApplyToWavResponse(audioBytes, unit);
            
            await TtsPlayback.PlayWavBytesAsync(audioBytes, cancellationToken);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[TTS error: {ex.Message}]");
        }
    }

    /// <summary>
    /// Maps the voice profile's controller type to an AtcUnit for audio effects lookup.
    /// </summary>
    private static AtcUnit MapControllerTypeToUnit(VoiceProfile? profile)
    {
        if (profile?.ControllerTypes == null || !profile.ControllerTypes.Any())
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
