using System.Text.Json;

namespace AtcNavDataDemo.Config;

public sealed class UserConfig
{
    public string SimBriefUsername { get; set; } = string.Empty;
}

public static class UserConfigStore
{
    private const string ConfigFileName = "userconfig.json";

    public static UserConfig Load()
    {
        try
        {
            if (!File.Exists(ConfigFileName))
                return new UserConfig();

            var json = File.ReadAllText(ConfigFileName);
            var config = JsonSerializer.Deserialize<UserConfig>(json);
            return config ?? new UserConfig();
        }
        catch
        {
            // On any error, fall back to defaults rather than crash.
            return new UserConfig();
        }
    }

    public static void Save(UserConfig config)
    {
        var json = JsonSerializer.Serialize(config, new JsonSerializerOptions
        {
            WriteIndented = true
        });
        File.WriteAllText(ConfigFileName, json);
    }
}
