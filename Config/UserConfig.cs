using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace AtcNavDataDemo.Config;

public sealed class UserConfig
{
    public string SimBriefUsername { get; set; } = string.Empty;
    public AudioConfig Audio { get; set; } = new();
    public TtsConfig Tts { get; set; } = new();
    public SttConfig Stt { get; set; } = new();
    public string AtcTextProvider { get; set; } = "openai";
}

public sealed class AudioConfig
{
    public string? MicrophoneDeviceId { get; set; }
    public string? OutputDeviceId { get; set; }
    
    /// <summary>
    /// Software mic gain in dB. Range: -12 to +12. Default: 0.
    /// </summary>
    public double MicGainDb { get; set; } = 0.0;
    
    /// <summary>
    /// ATC audio output device ID. If null, uses system default.
    /// </summary>
    public string? AtcOutputDeviceId { get; set; }
    
    /// <summary>
    /// ATC audio volume as percentage (0-100). Default: 100.
    /// </summary>
    public int AtcVolumePercent { get; set; } = 100;
}

public sealed class TtsConfig
{
    public bool VoiceLabEnabled { get; set; } = true;
    public string VoiceLabBaseUrl { get; set; } = "http://127.0.0.1:8008";
}

public sealed class SttConfig
{
    /// <summary>
    /// Whisper-fast STT service port. Default: 8766.
    /// </summary>
    public int WhisperFastPort { get; set; } = 8766;
}

public static class UserConfigStore
{
    private const string ConfigFileName = "userconfig.json";

    public static UserConfig Load()
    {
        var path = GetPrimaryPath();

        try
        {
            if (!File.Exists(path))
            {
                var defaults = new UserConfig();
                Save(defaults);
                return defaults;
            }

            var json = File.ReadAllText(path);
            var config = JsonSerializer.Deserialize<UserConfig>(json);
            if (config == null)
            {
                var defaults = new UserConfig();
                Save(defaults);
                return defaults;
            }
            if (config.Audio == null)
                config.Audio = new AudioConfig();
            if (config.Tts == null)
                config.Tts = new TtsConfig();
            if (config.Stt == null)
                config.Stt = new SttConfig();
            if (string.IsNullOrWhiteSpace(config.AtcTextProvider))
                config.AtcTextProvider = "openai";
            return config;
        }
        catch
        {
            var defaults = new UserConfig();
            Save(defaults);
            return defaults;
        }
    }

    public static void Save(UserConfig config)
    {
        var json = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
        var primaryPath = GetPrimaryPath();
        TryWrite(primaryPath, json);
    }

    private static bool TryWrite(string path, string json)
    {
        try
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            File.WriteAllText(path, json);
            return true;
        }
        catch
        {
            // Swallow to avoid UI crashes; we will fall back to a safer location.
            return false;
        }
    }

    private static string GetPrimaryPath()
    {
        var baseDir = AppContext.BaseDirectory;
        return Path.Combine(baseDir, ConfigFileName);
    }
}
