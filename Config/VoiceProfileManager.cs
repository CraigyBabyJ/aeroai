using System;
using System.Collections.Generic;
using System.Linq;

namespace AtcNavDataDemo.Config;

public sealed class VoiceProfileManager
{
    private readonly IReadOnlyList<VoiceProfile> _profiles;
    private readonly VoiceConfig _fallbackConfig;

    public VoiceProfileManager(VoiceConfig fallbackConfig, IReadOnlyList<VoiceProfile>? profiles = null)
    {
        _fallbackConfig = fallbackConfig ?? throw new ArgumentNullException(nameof(fallbackConfig));
        _profiles = profiles ?? Array.Empty<VoiceProfile>();
    }

    public VoiceProfile GetProfileFor(string? departureIcao, string controllerType, string? preferredProfileId = null)
    {
        // 1) Preferred profile
        if (!string.IsNullOrWhiteSpace(preferredProfileId))
        {
            var preferred = _profiles.FirstOrDefault(p => string.Equals(p.Id, preferredProfileId, StringComparison.OrdinalIgnoreCase));
            if (preferred != null)
            {
                return preferred;
            }
        }

        // 2) Region + controller match (supports prefix)
        if (!string.IsNullOrWhiteSpace(departureIcao))
        {
            var matches = _profiles.Where(p => RegionMatches(p.RegionCodes, departureIcao) &&
                                               p.ControllerTypes.Any(ct => string.Equals(ct, controllerType, StringComparison.OrdinalIgnoreCase)));
            var regionProfile = matches.FirstOrDefault();
            if (regionProfile != null)
            {
                return regionProfile;
            }
        }

        // 3) Default profile
        var defaultProfile = _profiles.FirstOrDefault(p => string.Equals(p.Id, "default", StringComparison.OrdinalIgnoreCase));
        if (defaultProfile != null)
        {
            return defaultProfile;
        }

        // 4) Fallback synthesized from config
        Console.WriteLine("[VoiceProfiles] No matching profile; falling back to VoiceConfig.");
        return new VoiceProfile
        {
            Id = "fallback",
            DisplayName = "Fallback (VoiceConfig)",
            TtsModel = _fallbackConfig.Model,
            TtsVoice = _fallbackConfig.Voice,
            SpeakingRate = _fallbackConfig.Speed
        };
    }

    private static bool RegionMatches(IEnumerable<string> regions, string icao)
    {
        foreach (var r in regions)
        {
            if (string.IsNullOrWhiteSpace(r))
                continue;
            if (icao.StartsWith(r, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }
}
