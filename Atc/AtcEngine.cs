using System.Text;
using AeroAI.Models;
using AtcNavDataDemo.Models;

namespace AtcNavDataDemo.Atc;

public class AtcEngine
{
    public string GenerateIfrClearance(
        string callsign,
        string originIcao,
        string destinationIcao,
        NavRunwaySummary departureRunway,
        SidSelectionResult sidResult,
        EnrouteRoute? enrouteRoute,
        StarSelectionResult starResult)
    {
        var sb = new StringBuilder();

        // Standard IFR clearance format
        sb.AppendLine($"{callsign}, cleared to {destinationIcao},");
        
        // Route clearance
        var routeParts = new List<string>();
        
        // SID
        if (sidResult.Mode == ProcedureSelectionMode.Published && sidResult.SelectedSid is not null)
        {
            routeParts.Add($"via the {sidResult.SelectedSid.ProcedureIdentifier} departure");
            if (!string.IsNullOrWhiteSpace(sidResult.MatchingExitFix))
            {
                routeParts.Add($"then {sidResult.MatchingExitFix}");
            }
        }
        else
        {
            routeParts.Add("via radar vectors");
        }

        // Enroute waypoints
        if (enrouteRoute is not null && enrouteRoute.WaypointIdentifiers.Count > 0)
        {
            routeParts.Add($"then {string.Join(" ", enrouteRoute.WaypointIdentifiers)}");
        }

        // STAR
        if (starResult.Mode == ProcedureSelectionMode.Published && starResult.SelectedStar is not null)
        {
            routeParts.Add($"then the {starResult.SelectedStar.ProcedureIdentifier} arrival");
        }
        else
        {
            routeParts.Add("then expect vectors");
        }

        sb.AppendLine(string.Join(", ", routeParts) + ".");
        
        // Initial altitude
        var initialAltitude = enrouteRoute?.WaypointIdentifiers.Count > 0 ? "FL" + (enrouteRoute.WaypointIdentifiers.Count * 10 + 200).ToString("D3") : "FL250";
        sb.AppendLine($"Maintain {initialAltitude}.");
        
        // Squawk code (random but realistic)
        var random = new Random();
        var squawk = $"{random.Next(1, 8)}{random.Next(0, 8)}{random.Next(0, 8)}{random.Next(0, 8)}";
        sb.AppendLine($"Squawk {squawk}.");
        
        // Departure runway
        sb.AppendLine($"Runway {departureRunway.RunwayIdentifier}, cleared for takeoff.");
        
        // Contact departure
        sb.AppendLine($"Contact departure on 121.9 when airborne.");

        return sb.ToString();
    }

    public string GenerateCompleteClearance(
        string callsign,
        string originIcao,
        string destinationIcao,
        NavRunwaySummary departureRunway,
        SidSelectionResult sidResult,
        EnrouteRoute? enrouteRoute,
        StarSelectionResult starResult,
        ApproachSelectionResult approachResult,
        NavRunwaySummary arrivalRunway)
    {
        var sb = new StringBuilder();

        // Departure clearance
        sb.AppendLine($"{callsign}, cleared to {destinationIcao}.");
        sb.AppendLine($"Runway {departureRunway.RunwayIdentifier}, cleared for takeoff.");

        if (sidResult.Mode == ProcedureSelectionMode.Published && sidResult.SelectedSid is not null)
        {
            sb.AppendLine($"After departure, fly the {sidResult.SelectedSid.ProcedureIdentifier} departure.");
            if (!string.IsNullOrWhiteSpace(sidResult.MatchingExitFix))
            {
                sb.AppendLine($"Then via {sidResult.MatchingExitFix}.");
            }
        }
        else
        {
            sb.AppendLine("After departure, expect vectors.");
        }

        // Enroute
        if (enrouteRoute is not null && enrouteRoute.WaypointIdentifiers.Count > 0)
        {
            sb.AppendLine($"Then via {string.Join(" ", enrouteRoute.WaypointIdentifiers)}.");
        }

        // Arrival
        if (starResult.Mode == ProcedureSelectionMode.Published && starResult.SelectedStar is not null)
        {
            if (!string.IsNullOrWhiteSpace(starResult.MatchingEntryFix))
            {
                sb.AppendLine($"Via {starResult.MatchingEntryFix}, expect the {starResult.SelectedStar.ProcedureIdentifier} arrival.");
            }
            else
            {
                sb.AppendLine($"Expect the {starResult.SelectedStar.ProcedureIdentifier} arrival.");
            }
        }
        else
        {
            sb.AppendLine("Expect vectors for arrival.");
        }

        // Approach
        if (approachResult.Mode == ProcedureSelectionMode.Published && approachResult.SelectedApproach is not null)
        {
            var approach = approachResult.SelectedApproach;
            sb.AppendLine($"Cleared {approach.ApproachTypeCode} {approach.ProcedureIdentifier} approach runway {arrivalRunway.RunwayIdentifier}.");
        }
        else
        {
            sb.AppendLine($"Cleared visual approach runway {arrivalRunway.RunwayIdentifier}.");
        }

        sb.AppendLine("Report runway in sight.");

        return sb.ToString();
    }

    public string DescribeApproach(Procedure procedure, string callsign)
    {
        var sb = new StringBuilder();

        var runway = InferRunwayFromProcedureId(procedure.ProcedureIdentifier);
        var transition = string.IsNullOrWhiteSpace(procedure.TransitionIdentifier)
            ? "direct"
            : procedure.TransitionIdentifier;

        if (!string.IsNullOrEmpty(runway))
        {
            sb.AppendLine($"{callsign}, cleared approach runway {runway} via {transition}.");
        }
        else
        {
            sb.AppendLine(
                $"{callsign}, cleared {procedure.RouteType} {procedure.ProcedureIdentifier} approach via {transition}.");
        }

        for (int i = 0; i < procedure.Legs.Count; i++)
        {
            var leg = procedure.Legs[i];

            var altitudePhrase = BuildAltitudePhrase(leg);
            var speedPhrase = BuildSpeedPhrase(leg);

            if (string.IsNullOrEmpty(altitudePhrase) && string.IsNullOrEmpty(speedPhrase))
                continue;

            var prefix = i == 0 ? "Cross" : "Then cross";
            var line = $"{prefix} {leg.WaypointIdentifier}";

            if (!string.IsNullOrEmpty(altitudePhrase))
            {
                line += $" {altitudePhrase}";
                if (!string.IsNullOrEmpty(speedPhrase))
                {
                    line += $", {speedPhrase}";
                }
            }
            else if (!string.IsNullOrEmpty(speedPhrase))
            {
                line += $" {speedPhrase}";
            }

            line += ".";
            sb.AppendLine(line);
        }

        sb.AppendLine("Report runway in sight.");
        return sb.ToString();
    }

    private static string BuildAltitudePhrase(ProcedureLeg leg)
    {
        // No constraint if altitudes are zero or type is None
        if (leg.AltitudeConstraintType == AltitudeConstraintType.None &&
            (leg.Altitude1 <= 0 && leg.Altitude2 <= 0))
        {
            return string.Empty;
        }

        int alt1 = leg.Altitude1;
        int alt2 = leg.Altitude2;

        return leg.AltitudeConstraintType switch
        {
            AltitudeConstraintType.AtOrAbove when alt1 > 0 =>
                $"at or above {alt1} feet",
            AltitudeConstraintType.AtOrBelow when alt1 > 0 =>
                $"at or below {alt1} feet",
            AltitudeConstraintType.Between when alt1 > 0 && alt2 > 0 =>
                $"between {alt1} and {alt2} feet",
            AltitudeConstraintType.At when alt1 > 0 =>
                $"maintain {alt1} feet",
            _ when alt1 > 0 =>
                $"maintain {alt1} feet",
            _ => string.Empty
        };
    }

    private static string BuildSpeedPhrase(ProcedureLeg leg)
    {
        if (leg.SpeedLimit <= 0)
            return string.Empty;

        var desc = leg.SpeedLimitDescription?.ToUpperInvariant() ?? string.Empty;

        if (desc.Contains("MIN"))
        {
            return $"at least {leg.SpeedLimit} knots";
        }

        if (desc.Contains("MAX") || desc.Contains("AT OR BELOW") || desc.Contains("BELOW"))
        {
            return $"at or below {leg.SpeedLimit} knots";
        }

        // Generic phrase if description is missing/unknown
        return $"maintain {leg.SpeedLimit} knots or less";
    }

    private static string InferRunwayFromProcedureId(string procedureId)
    {
        if (string.IsNullOrWhiteSpace(procedureId))
            return string.Empty;

        // Very simple heuristic: take trailing 2–3 digits as runway.
        var trimmed = procedureId.Trim();
        var digits = new Stack<char>();

        for (int i = trimmed.Length - 1; i >= 0; i--)
        {
            char c = trimmed[i];
            if (char.IsDigit(c))
            {
                digits.Push(c);
                if (digits.Count == 3)
                    break;
            }
            else if (digits.Count > 0)
            {
                break;
            }
        }

        if (digits.Count == 0)
            return string.Empty;

        var runway = new string(digits.ToArray());
        return runway;
    }
}
