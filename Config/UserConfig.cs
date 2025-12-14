using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
        var candidates = BuildCandidatePaths();
        var path = candidates.FirstOrDefault(File.Exists) ?? candidates.FirstOrDefault() ?? GetAppDataPath();

        try
        {
            if (!File.Exists(path))
                return new UserConfig();

            var json = File.ReadAllText(path);
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
        var json = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
        var candidates = BuildCandidatePaths();
        var primaryPath = candidates.FirstOrDefault(File.Exists) ?? candidates.FirstOrDefault() ?? GetAppDataPath();

        if (!TryWrite(primaryPath, json))
        {
            var fallback = GetAppDataPath();
            TryWrite(fallback, json);
        }
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

    private static List<string> BuildCandidatePaths()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var paths = new List<string>();

        void AddChain(string? start)
        {
            if (string.IsNullOrWhiteSpace(start) || !Directory.Exists(start))
                return;

            try
            {
                var dir = new DirectoryInfo(start);
                while (dir != null)
                {
                    var candidate = Path.Combine(dir.FullName, ConfigFileName);
                    if (seen.Add(candidate))
                        paths.Add(candidate);

                    dir = dir.Parent;
                }
            }
            catch
            {
                // ignore traversal issues
            }
        }

        AddChain(Directory.GetCurrentDirectory());
        AddChain(AppContext.BaseDirectory);

        var appDataPath = GetAppDataPath();
        if (seen.Add(appDataPath))
            paths.Add(appDataPath);

        return paths;
    }

    private static string GetAppDataPath()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var appDir = Path.Combine(appData, "AeroAI");
        return Path.Combine(appDir, ConfigFileName);
    }
}
