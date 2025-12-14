using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace AtcNavDataDemo.Config;

public sealed class VoiceProfile
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("display_name")]
    public string DisplayName { get; set; } = string.Empty;

    [JsonPropertyName("tts_model")]
    public string? TtsModel { get; set; }

    [JsonPropertyName("tts_voice")]
    public string? TtsVoice { get; set; }

    [JsonPropertyName("style_hint")]
    public string? StyleHint { get; set; }

    [JsonPropertyName("speaking_rate")]
    public double? SpeakingRate { get; set; }

    [JsonPropertyName("region_codes")]
    public List<string> RegionCodes { get; set; } = new List<string>();

    [JsonPropertyName("controller_types")]
    public List<string> ControllerTypes { get; set; } = new List<string>();
}
