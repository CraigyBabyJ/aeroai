using System;
using System.Collections.Generic;
using System.Linq;

namespace AeroAI.Atc;

public static class ControllerFlowHelper
{
    private static readonly AtcUnit[] DepartureFlow =
    {
        AtcUnit.ClearanceDelivery,
        AtcUnit.Ground,
        AtcUnit.Tower,
        AtcUnit.Departure,
        AtcUnit.Center
    };

    private static readonly AtcUnit[] ArrivalFlow =
    {
        AtcUnit.Center,
        AtcUnit.Approach,
        AtcUnit.Tower,
        AtcUnit.Ground
    };

    public static IReadOnlyList<AtcUnit> GetFlowOrder(bool isArrival)
    {
        return isArrival ? ArrivalFlow : DepartureFlow;
    }

    public static AtcUnit? SuggestNext(AtcUnit current, bool isArrival)
    {
        var flow = isArrival ? ArrivalFlow : DepartureFlow;
        var index = Array.IndexOf(flow, current);
        if (index < 0 || index + 1 >= flow.Length)
            return null;
        return flow[index + 1];
    }

    public static string ToRoleLabel(AtcUnit unit)
    {
        return unit switch
        {
            AtcUnit.ClearanceDelivery => "Delivery",
            AtcUnit.Ground => "Ground",
            AtcUnit.Tower => "Tower",
            AtcUnit.Departure => "Departure",
            AtcUnit.Center => "Center",
            AtcUnit.Approach => "Approach",
            AtcUnit.Arrival => "Arrival",
            _ => unit.ToString()
        };
    }

    public static AtcUnit? FromRoleLabel(string? role)
    {
        if (string.IsNullOrWhiteSpace(role))
            return null;

        var key = role.Trim().ToLowerInvariant();
        return key switch
        {
            "clearance" => AtcUnit.ClearanceDelivery,
            "delivery" => AtcUnit.ClearanceDelivery,
            "ground" => AtcUnit.Ground,
            "tower" => AtcUnit.Tower,
            "departure" => AtcUnit.Departure,
            "center" => AtcUnit.Center,
            "approach" => AtcUnit.Approach,
            "arrival" => AtcUnit.Arrival,
            _ => null
        };
    }

    public static ControllerSuggestion? SuggestNextAvailable(
        AtcUnit current,
        bool isArrival,
        IReadOnlyList<(string Role, double Frequency)> available)
    {
        var flow = GetFlowOrder(isArrival).Select(ToRoleLabel).ToList();
        var currentLabel = ToRoleLabel(current);
        var currentIndex = flow.FindIndex(role => role.Equals(currentLabel, StringComparison.OrdinalIgnoreCase));
        if (currentIndex < 0)
            return null;

        for (var i = currentIndex + 1; i < flow.Count; i++)
        {
            var targetRole = flow[i];
            var match = available.FirstOrDefault(item => item.Role.Equals(targetRole, StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrWhiteSpace(match.Role))
            {
                return new ControllerSuggestion(match.Role, match.Frequency);
            }
        }

        return null;
    }
}

public sealed class ControllerSuggestion
{
    public ControllerSuggestion(string role, double frequencyMhz)
    {
        Role = role;
        FrequencyMhz = frequencyMhz;
    }

    public string Role { get; }
    public double FrequencyMhz { get; }
}
