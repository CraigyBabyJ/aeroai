using System;

namespace AtcNavDataDemo.Config;

public static class VoiceConfigLoader
{
    /// <summary>
    /// Build VoiceConfig from environment variables. Defaults to disabled if no API key.
    /// </summary>
    public static VoiceConfig LoadFromEnvironment()
    {
        var enabledVar = Environment.GetEnvironmentVariable("AEROAI_TTS_ENABLED");
        bool enabled = string.Equals(enabledVar, "true", StringComparison.OrdinalIgnoreCase)
                       || string.Equals(enabledVar, "1", StringComparison.OrdinalIgnoreCase)
                       || string.Equals(enabledVar, "yes", StringComparison.OrdinalIgnoreCase);

        string apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY") ?? string.Empty;
        string model = Environment.GetEnvironmentVariable("AEROAI_TTS_MODEL");
        string voice = Environment.GetEnvironmentVariable("AEROAI_TTS_VOICE");
        string apiBase = Environment.GetEnvironmentVariable("OPENAI_API_BASE")
                          ?? Environment.GetEnvironmentVariable("OPENAI_BASE_URL")
                          ?? "https://api.openai.com/v1";
        double speed = ParseSpeed(Environment.GetEnvironmentVariable("AEROAI_TTS_SPEED"));

        // If no API key is present, force-disable to keep text flow intact.
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            enabled = false;
        }

        return new VoiceConfig
        {
            Enabled = enabled,
            ApiKey = apiKey,
            Model = string.IsNullOrWhiteSpace(model) ? "gpt-4o-mini-tts" : model,
            Voice = string.IsNullOrWhiteSpace(voice) ? "alloy" : voice,
            ApiBase = apiBase,
            Speed = speed
        };
    }

    private static double ParseSpeed(string? value)
    {
        if (double.TryParse(value, out var s))
        {
            // Clamp to OpenAI supported range 0.25–4.0
            if (s < 0.25) s = 0.25;
            if (s > 4.0) s = 4.0;
            return s;
        }
        return 1.0;
    }
}
