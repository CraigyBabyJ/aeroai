using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using AtcNavDataDemo.Config;

namespace AeroAI.Audio;

public sealed class VoiceLabTtsClient : ITtsClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
    private static readonly JsonSerializerOptions DebugJsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true
    };

    private readonly HttpClient _http;
    private readonly Uri _baseUri;
    private readonly Action<string>? _onDebug;

    public VoiceLabTtsClient(HttpClient http, UserConfig userConfig, Action<string>? onDebug = null)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        if (userConfig == null)
            throw new ArgumentNullException(nameof(userConfig));
        _onDebug = onDebug;

        var baseUrl = userConfig.Tts?.VoiceLabBaseUrl;
        if (string.IsNullOrWhiteSpace(baseUrl))
            baseUrl = "http://127.0.0.1:8008";

        _baseUri = new Uri(baseUrl.TrimEnd('/') + "/");
    }

    public async Task<TtsHealth> HealthAsync(CancellationToken ct = default)
    {
        try
        {
            using var response = await _http.GetAsync(new Uri(_baseUri, "health"), ct);
            if (!response.IsSuccessStatusCode)
                return new TtsHealth { Online = false, Detail = response.StatusCode.ToString() };

            var json = await response.Content.ReadAsStringAsync(ct);
            if (string.IsNullOrWhiteSpace(json))
                return new TtsHealth { Online = true };

            try
            {
                var data = JsonSerializer.Deserialize<Dictionary<string, object>>(json);
                return new TtsHealth { Online = true, Data = data };
            }
            catch
            {
                return new TtsHealth { Online = true };
            }
        }
        catch (Exception ex)
        {
            return new TtsHealth { Online = false, Detail = ex.Message };
        }
    }

    public async Task<TtsResult> SynthesizeAsync(TtsRequest request, CancellationToken ct = default)
    {
        if (request == null)
            throw new ArgumentNullException(nameof(request));

        var payload = new VoiceLabTtsPayload
        {
            Text = request.Text,
            VoiceId = string.IsNullOrWhiteSpace(request.VoiceId) ? "auto" : request.VoiceId,
            Role = request.Role,
            Language = string.IsNullOrWhiteSpace(request.Language) ? "en" : request.Language,
            Speed = request.Speed,
            Format = string.IsNullOrWhiteSpace(request.Format) ? "wav" : request.Format,
            RadioProfile = request.RadioProfile,
            RadioIntensity = request.RadioIntensity,
            SoftPauseMs = request.SoftPauseMs,
            HardPauseMs = request.HardPauseMs,
            AirportIcao = request.AirportIcao,
            RegionPrefix = request.RegionPrefix,
            IsoCountry = request.IsoCountry,
            IsoRegion = request.IsoRegion
        };

        var radioProfile = string.IsNullOrWhiteSpace(payload.RadioProfile) ? "none" : payload.RadioProfile;
        var radioIntensity = payload.RadioIntensity.HasValue
            ? payload.RadioIntensity.Value.ToString("0.00", CultureInfo.InvariantCulture)
            : "none";
        string payloadJson = JsonSerializer.Serialize(payload, DebugJsonOptions);
        _onDebug?.Invoke($"[VoiceLab] request: POST {new Uri(_baseUri, "tts")} (radio_profile={radioProfile}, radio_intensity={radioIntensity})\n{payloadJson}");

        using var requestMessage = new HttpRequestMessage(HttpMethod.Post, new Uri(_baseUri, "tts"));
        requestMessage.Content = new StringContent(JsonSerializer.Serialize(payload, JsonOptions), Encoding.UTF8, "application/json");

        using var response = await _http.SendAsync(requestMessage, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!response.IsSuccessStatusCode)
        {
            var detail = await response.Content.ReadAsStringAsync(ct);
            _onDebug?.Invoke($"[VoiceLab] response: {(int)response.StatusCode} {response.ReasonPhrase}\n{detail}");
            throw new InvalidOperationException($"VoiceLab TTS failed: {response.StatusCode} {detail}");
        }

        var audio = await response.Content.ReadAsByteArrayAsync(ct);
        var resolvedVoiceId = ReadHeader(response, "X-Resolved-Voice-Id");
        var resolvedEngine = ReadHeader(response, "X-Resolved-Engine");
        var cacheStatus = ReadHeader(response, "X-Cache");
        var normalized = ReadHeader(response, "X-Normalized");
        _onDebug?.Invoke($"[VoiceLab] response: {(int)response.StatusCode} {response.ReasonPhrase}, bytes={audio.Length}, resolved_voice={resolvedVoiceId ?? "unknown"}, engine={resolvedEngine ?? "unknown"}, cache={cacheStatus ?? "unknown"}, normalized={normalized ?? "unknown"}");
        return new TtsResult
        {
            WavBytes = audio,
            ResolvedVoiceId = resolvedVoiceId,
            ResolvedEngine = resolvedEngine,
            CacheStatus = cacheStatus
        };
    }

    private static string? ReadHeader(HttpResponseMessage response, string headerName)
    {
        if (response.Headers.TryGetValues(headerName, out var values))
            return values.FirstOrDefault();
        return null;
    }

    private sealed class VoiceLabTtsPayload
    {
        [JsonPropertyName("text")]
        public string Text { get; set; } = string.Empty;

        [JsonPropertyName("voice_id")]
        public string VoiceId { get; set; } = "auto";

        [JsonPropertyName("role")]
        public string? Role { get; set; }

        [JsonPropertyName("language")]
        public string Language { get; set; } = "en";

        [JsonPropertyName("speed")]
        public double Speed { get; set; } = 1.0;

        [JsonPropertyName("radio_profile")]
        public string? RadioProfile { get; set; }

        [JsonPropertyName("radio_intensity")]
        public double? RadioIntensity { get; set; }

        [JsonPropertyName("format")]
        public string Format { get; set; } = "wav";

        [JsonPropertyName("soft_pause_ms")]
        public int? SoftPauseMs { get; set; }

        [JsonPropertyName("hard_pause_ms")]
        public int? HardPauseMs { get; set; }

        [JsonPropertyName("airport_icao")]
        public string? AirportIcao { get; set; }

        [JsonPropertyName("region_prefix")]
        public string? RegionPrefix { get; set; }

        [JsonPropertyName("iso_country")]
        public string? IsoCountry { get; set; }

        [JsonPropertyName("iso_region")]
        public string? IsoRegion { get; set; }
    }
}
