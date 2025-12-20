using System;
using System.Collections.Generic;

namespace AeroAI.Data;

public sealed class ControllerFrequency
{
    public ControllerFrequency(string role, double frequencyMhz)
    {
        Role = role;
        FrequencyMhz = frequencyMhz;
    }

    public string Role { get; }
    public double FrequencyMhz { get; }
}

public static class AirportFrequencyService
{
    public static IReadOnlyList<ControllerFrequency> GetControllers(string? icao)
    {
        if (!AirportFrequencies.TryGetFrequencies(icao, out var freqs))
            return Array.Empty<ControllerFrequency>();

        var list = new List<ControllerFrequency>();
        Add(list, "Clearance", freqs.Clearance);
        Add(list, "Ground", freqs.Ground);
        Add(list, "Tower", freqs.Tower);
        Add(list, "Departure", freqs.Departure);
        Add(list, "Approach", freqs.Approach);
        return list;
    }

    private static void Add(List<ControllerFrequency> list, string role, double? frequency)
    {
        if (!frequency.HasValue || frequency.Value <= 0)
            return;
        list.Add(new ControllerFrequency(role, frequency.Value));
    }
}
