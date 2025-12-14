using System;

namespace AtcNavDataDemo.Config;

public sealed class VoiceConfig
{
    public bool Enabled { get; init; }
    public string ApiKey { get; init; } = string.Empty;
    public string Model { get; init; } = "gpt-4o-mini-tts";
    public string Voice { get; init; } = "alloy";
    public string ApiBase { get; init; } = "https://api.openai.com/v1";
    public double Speed { get; init; } = 1.0; // 0.25–4.0 supported by OpenAI
}
