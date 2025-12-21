using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace AeroAI.Audio;

public interface ITtsClient
{
    Task<TtsHealth> HealthAsync(CancellationToken ct = default);
    Task<TtsResult> SynthesizeAsync(TtsRequest request, CancellationToken ct = default);
}

public sealed class TtsRequest
{
    public string Text { get; init; } = string.Empty;
    public string VoiceId { get; init; } = "auto";
    public string? Role { get; init; }
    public string? SessionId { get; init; }
    public string Language { get; init; } = "en";
    public double Speed { get; init; } = 1.0;
    public string Format { get; init; } = "wav";
    public string? RadioProfile { get; init; }
    public double? RadioIntensity { get; init; }
    public int? SoftPauseMs { get; init; }
    public int? HardPauseMs { get; init; }
    public string? AirportIcao { get; init; }
    public string? RegionPrefix { get; init; }
    public string? IsoCountry { get; init; }
    public string? IsoRegion { get; init; }
}

public sealed class TtsResult
{
    public byte[] WavBytes { get; init; } = new byte[0];
    public string? ResolvedVoiceId { get; init; }
    public string? ResolvedEngine { get; init; }
    public string? CacheStatus { get; init; }
}

public sealed class TtsHealth
{
    public bool Online { get; init; }
    public string? Detail { get; init; }
    public IReadOnlyDictionary<string, object>? Data { get; init; }
}
