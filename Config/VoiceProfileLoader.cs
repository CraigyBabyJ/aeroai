using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace AtcNavDataDemo.Config;

public static class VoiceProfileLoader
{
    public static IReadOnlyList<VoiceProfile> LoadProfiles(string? basePath = null)
    {
        var profiles = new List<VoiceProfile>();
        try
        {
            string root = basePath ?? Path.Combine(AppContext.BaseDirectory, "voices");
            if (!Directory.Exists(root))
            {
                Console.WriteLine("[VoiceProfiles] voices folder not found, using defaults.");
                return profiles;
            }

            foreach (var file in Directory.EnumerateFiles(root, "*.json"))
            {
                try
                {
                    var json = File.ReadAllText(file);
                    var profile = JsonSerializer.Deserialize<VoiceProfile>(json);
                    if (profile != null && !string.IsNullOrWhiteSpace(profile.Id))
                    {
                        profiles.Add(profile);
                    }
                }
                catch
                {
                    Console.WriteLine($"[VoiceProfiles] Failed to load profile {file}, skipping.");
                }
            }
        }
        catch
        {
            Console.WriteLine("[VoiceProfiles] Error loading profiles, using defaults.");
        }

        return profiles;
    }

    public static VoiceProfile? SelectProfile(IReadOnlyList<VoiceProfile> profiles, string? regionIcao, string? controllerRole, string? overrideId = null)
    {
        if (profiles == null || profiles.Count == 0)
        {
            return null;
        }

        // Explicit override by id
        if (!string.IsNullOrWhiteSpace(overrideId))
        {
            var match = profiles.FirstOrDefault(p => string.Equals(p.Id, overrideId, StringComparison.OrdinalIgnoreCase));
            if (match != null)
            {
                return match;
            }
        }

        // Region + controller role match
        if (!string.IsNullOrWhiteSpace(regionIcao))
        {
            var regionMatches = profiles.Where(p => p.RegionCodes.Any(r => string.Equals(r, regionIcao, StringComparison.OrdinalIgnoreCase)));
            if (!string.IsNullOrWhiteSpace(controllerRole))
            {
                var match = regionMatches.FirstOrDefault(p => p.ControllerTypes.Any(ct => string.Equals(ct, controllerRole, StringComparison.OrdinalIgnoreCase)));
                if (match != null)
                {
                    return match;
                }
            }

            var regionOnly = regionMatches.FirstOrDefault();
            if (regionOnly != null)
            {
                return regionOnly;
            }
        }

        // Default profile
        var defaultProfile = profiles.FirstOrDefault(p => string.Equals(p.Id, "default", StringComparison.OrdinalIgnoreCase));
        return defaultProfile ?? profiles.FirstOrDefault();
    }
}
