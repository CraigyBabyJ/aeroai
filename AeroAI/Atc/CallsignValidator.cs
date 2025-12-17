using System;
using System.Linq;

namespace AeroAI.Atc;

public static class CallsignValidator
{
    public static bool IsPresent(string text, FlightContext? flight, bool allowAnywhere = false)
    {
        if (flight == null)
            return true;

        if (CallsignMatcher.IsRecognized(text, flight))
            return true;

        if (allowAnywhere)
        {
            var info = CallsignMatcher.BuildContextInfo(flight);
            var normalizedInput = Normalize(text);
            foreach (var variant in info.ExpectedVariants)
            {
                var normVariant = Normalize(variant);
                if (!string.IsNullOrWhiteSpace(normVariant) && normalizedInput.Contains(normVariant))
                    return true;
            }
            return false;
        }

        var candidate = flight.Callsign ?? flight.RawCallsign ?? string.Empty;
        if (string.IsNullOrWhiteSpace(candidate))
            return true;

        var normInput = NormalizeEnd(text);
        var normCall = NormalizeEnd(candidate);
        if (normInput.EndsWith(normCall, StringComparison.OrdinalIgnoreCase))
            return true;

        var flightNumber = flight.FlightNumber ?? string.Empty;
        var airlinePrefix = (flight.AirlineIcao ?? string.Empty).ToUpperInvariant();
        var airlineName = (flight.AirlineName ?? string.Empty).ToUpperInvariant();

        bool hasNumber = !string.IsNullOrWhiteSpace(flightNumber) && normInput.Contains(flightNumber.Trim());
        bool hasAirlinePrefix = (!string.IsNullOrWhiteSpace(airlinePrefix) && normInput.Contains(airlinePrefix.Substring(0, Math.Min(2, airlinePrefix.Length))))
            || (!string.IsNullOrWhiteSpace(airlineName) && airlineName.Length >= 2 && normInput.Contains(airlineName.Substring(0, 2)));

        return hasNumber && hasAirlinePrefix;
    }

    private static string Normalize(string value)
    {
        return new string(value.ToUpperInvariant()
            .Where(char.IsLetterOrDigit)
            .ToArray());
    }

    private static string NormalizeEnd(string value)
    {
        return value.Trim().TrimEnd('.', ',', ';', '?', '!', ' ').ToUpperInvariant();
    }
}
