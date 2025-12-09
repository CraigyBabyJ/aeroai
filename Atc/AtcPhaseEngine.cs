using System.Text;
using AeroAI.Models;

namespace AtcNavDataDemo.Atc;

/// <summary>
/// Generates complete ATC communications for all phases of an IFR flight.
/// Handles: Clearance → Push → Taxi → Departure → Enroute → Arrival → Landing → Taxi In
/// </summary>
public class AtcPhaseEngine
{
    private readonly Random _random = new();

    public string GenerateFullFlightFlow(
        string callsign,
        string originIcao,
        string destinationIcao,
        NavRunwaySummary departureRunway,
        SidSelectionResult sidResult,
        EnrouteRoute? enrouteRoute,
        StarSelectionResult starResult,
        ApproachSelectionResult approachResult,
        NavRunwaySummary arrivalRunway,
        WeatherInfo originWeather,
        WeatherInfo destinationWeather,
        int cruiseFlightLevel)
    {
        var sb = new StringBuilder();
        
        sb.AppendLine("═══════════════════════════════════════════════════════════════");
        sb.AppendLine("           COMPLETE IFR FLIGHT - ATC COMMUNICATIONS");
        sb.AppendLine("═══════════════════════════════════════════════════════════════");
        sb.AppendLine();
        sb.AppendLine($"Aircraft: {callsign}");
        sb.AppendLine($"Route: {originIcao} → {destinationIcao}");
        sb.AppendLine($"Departure Runway: {departureRunway.RunwayIdentifier} (AeroAI-selected based on weather)");
        sb.AppendLine($"Arrival Runway: {arrivalRunway.RunwayIdentifier} (AeroAI-selected based on weather)");
        sb.AppendLine();
        sb.AppendLine("═══════════════════════════════════════════════════════════════");
        sb.AppendLine();

        // PHASE 1: CLEARANCE DELIVERY
        sb.AppendLine("PHASE 1 – CLEARANCE DELIVERY (PRE-DEPARTURE)");
        sb.AppendLine("───────────────────────────────────────────────────────────────");
        sb.AppendLine($"Context: At gate, {originIcao}. SimBrief route filed, but no SID or runway preselected.");
        sb.AppendLine();
        sb.AppendLine("PILOT → CLEARANCE DELIVERY");
        sb.AppendLine($"\"{GetClearanceDeliveryName(originIcao)} Clearance, {FormatCallsign(callsign)}, stand [GATE], IFR to {destinationIcao}, information [INFO], ready to copy.\"");
        sb.AppendLine();
        sb.AppendLine("CLEARANCE");
        var clearance = GenerateClearanceDelivery(
            callsign, originIcao, destinationIcao, departureRunway, sidResult, enrouteRoute, cruiseFlightLevel);
        sb.AppendLine(clearance);
        sb.AppendLine();
        sb.AppendLine("PILOT");
        sb.AppendLine($"\"{GetReadback(callsign, departureRunway, sidResult, cruiseFlightLevel)}, {FormatCallsign(callsign)}.\"");
        sb.AppendLine();
        sb.AppendLine("CLEARANCE");
        sb.AppendLine($"\"{FormatCallsign(callsign)}, readback correct.\"");
        sb.AppendLine();
        sb.AppendLine();

        // PHASE 2: PUSH & TAXI
        sb.AppendLine("PHASE 2 – PUSH & TAXI (GROUND)");
        sb.AppendLine("───────────────────────────────────────────────────────────────");
        sb.AppendLine();
        sb.AppendLine("PILOT → GROUND");
        sb.AppendLine($"\"{GetGroundName(originIcao)} Ground, {FormatCallsign(callsign)}, stand [GATE], request push and start.\"");
        sb.AppendLine();
        sb.AppendLine("GROUND");
        sb.AppendLine($"\"{FormatCallsign(callsign)}, push and start approved, facing {GetPushDirection(departureRunway)}.\"");
        sb.AppendLine();
        sb.AppendLine("PILOT");
        sb.AppendLine($"\"Push and start approved, facing {GetPushDirection(departureRunway)}, {FormatCallsign(callsign)}.\"");
        sb.AppendLine();
        sb.AppendLine("(After push & start completed)");
        sb.AppendLine();
        sb.AppendLine("PILOT → GROUND");
        sb.AppendLine($"\"{GetGroundName(originIcao)} Ground, {FormatCallsign(callsign)}, ready to taxi.\"");
        sb.AppendLine();
        sb.AppendLine("GROUND");
        sb.AppendLine($"\"{FormatCallsign(callsign)}, taxi to holding point runway {departureRunway.RunwayIdentifier} via [TAXI ROUTE], hold short runway {departureRunway.RunwayIdentifier}.\"");
        sb.AppendLine();
        sb.AppendLine("PILOT");
        sb.AppendLine($"\"Taxi to holding point runway {departureRunway.RunwayIdentifier} via [TAXI ROUTE], hold short runway {departureRunway.RunwayIdentifier}, {FormatCallsign(callsign)}.\"");
        sb.AppendLine();
        sb.AppendLine();

        // PHASE 3: LINE-UP & DEPARTURE
        sb.AppendLine("PHASE 3 – LINE-UP & DEPARTURE (TOWER)");
        sb.AppendLine("───────────────────────────────────────────────────────────────");
        sb.AppendLine();
        sb.AppendLine("PILOT → TOWER (at holding point)");
        sb.AppendLine($"\"{GetTowerName(originIcao)} Tower, {FormatCallsign(callsign)}, holding short runway {departureRunway.RunwayIdentifier}, ready for departure.\"");
        sb.AppendLine();
        sb.AppendLine("TOWER");
        sb.AppendLine($"\"{FormatCallsign(callsign)}, {GetTowerName(originIcao)} Tower, wind {originWeather.WindDirectionDegrees:D3} at {originWeather.WindSpeedKnots}, runway {departureRunway.RunwayIdentifier}, line up and wait.\"");
        sb.AppendLine();
        sb.AppendLine("PILOT");
        sb.AppendLine($"\"Line up and wait, runway {departureRunway.RunwayIdentifier}, {FormatCallsign(callsign)}.\"");
        sb.AppendLine();
        sb.AppendLine("(Aircraft lines up)");
        sb.AppendLine();
        sb.AppendLine("TOWER");
        var initialAltitude = GetInitialAltitude(cruiseFlightLevel);
        var sidName = sidResult.Mode == ProcedureSelectionMode.Published && sidResult.SelectedSid is not null
            ? sidResult.SelectedSid.ProcedureIdentifier
            : "vectors";
        sb.AppendLine($"\"{FormatCallsign(callsign)}, runway {departureRunway.RunwayIdentifier}, cleared for takeoff. Climb {initialAltitude}, {sidName} departure.\"");
        sb.AppendLine();
        sb.AppendLine("PILOT");
        sb.AppendLine($"\"Cleared for takeoff runway {departureRunway.RunwayIdentifier}, climb {initialAltitude}, {sidName} departure, {FormatCallsign(callsign)}.\"");
        sb.AppendLine();
        sb.AppendLine();

        // PHASE 4: INITIAL CLIMB & DEPARTURE
        sb.AppendLine("PHASE 4 – INITIAL CLIMB & DEPARTURE (DEPARTURE / RADAR)");
        sb.AppendLine("───────────────────────────────────────────────────────────────");
        sb.AppendLine();
        sb.AppendLine("(Shortly after takeoff)");
        sb.AppendLine();
        sb.AppendLine("TOWER");
        sb.AppendLine($"\"{FormatCallsign(callsign)}, contact {GetDepartureName(originIcao)} on {GetDepartureFrequency(originIcao)}, good flight.\"");
        sb.AppendLine();
        sb.AppendLine("PILOT");
        sb.AppendLine($"\"{GetDepartureFrequency(originIcao)}, thanks, {FormatCallsign(callsign)}.\"");
        sb.AppendLine();
        sb.AppendLine("PILOT → DEPARTURE");
        sb.AppendLine($"\"{GetDepartureName(originIcao)} Departure, {FormatCallsign(callsign)}, passing [ALTITUDE] for {initialAltitude}, {sidName}.\"");
        sb.AppendLine();
        sb.AppendLine("DEPARTURE");
        sb.AppendLine($"\"{FormatCallsign(callsign)}, {GetDepartureName(originIcao)} Departure, radar contact. Climb flight level {GetIntermediateAltitude(cruiseFlightLevel)}.\"");
        sb.AppendLine();
        sb.AppendLine("PILOT");
        sb.AppendLine($"\"Climb flight level {GetIntermediateAltitude(cruiseFlightLevel)}, {FormatCallsign(callsign)}.\"");
        sb.AppendLine();
        sb.AppendLine("(Later)");
        sb.AppendLine();
        sb.AppendLine("DEPARTURE / CENTER");
        sb.AppendLine($"\"{FormatCallsign(callsign)}, climb flight level {cruiseFlightLevel}.\"");
        sb.AppendLine();
        sb.AppendLine("PILOT");
        sb.AppendLine($"\"Climb flight level {cruiseFlightLevel}, {FormatCallsign(callsign)}.\"");
        sb.AppendLine();
        sb.AppendLine();

        // PHASE 5: TOP OF DESCENT / ARRIVAL SETUP
        sb.AppendLine("PHASE 5 – TOP OF DESCENT / ARRIVAL SETUP");
        sb.AppendLine("───────────────────────────────────────────────────────────────");
        sb.AppendLine($"Context: Approaching {destinationIcao}. AeroAI has determined:");
        sb.AppendLine($"  - Wind: {destinationWeather.WindDirectionDegrees:D3}°/{destinationWeather.WindSpeedKnots}kt → Runway {arrivalRunway.RunwayIdentifier} in use");
        var starName = starResult.Mode == ProcedureSelectionMode.Published && starResult.SelectedStar is not null
            ? starResult.SelectedStar.ProcedureIdentifier
            : "vectors";
        var approachName = approachResult.Mode == ProcedureSelectionMode.Published && approachResult.SelectedApproach is not null
            ? $"{approachResult.SelectedApproach.ProcedureIdentifier} approach"
            : "visual approach";
        sb.AppendLine($"  - STAR: {starName}");
        sb.AppendLine($"  - Approach: {approachName} runway {arrivalRunway.RunwayIdentifier}");
        sb.AppendLine();
        sb.AppendLine("CENTER → AEROAI");
        sb.AppendLine($"\"{FormatCallsign(callsign)}, expect runway {arrivalRunway.RunwayIdentifier} at {destinationIcao}, expect arrival {starName} then {approachName} runway {arrivalRunway.RunwayIdentifier}.\"");
        sb.AppendLine();
        sb.AppendLine("PILOT");
        sb.AppendLine($"\"Expect runway {arrivalRunway.RunwayIdentifier}, {starName} arrival then {approachName} runway {arrivalRunway.RunwayIdentifier}, {FormatCallsign(callsign)}.\"");
        sb.AppendLine();
        sb.AppendLine("(Later, starting descent)");
        sb.AppendLine();
        sb.AppendLine("CENTER");
        sb.AppendLine($"\"{FormatCallsign(callsign)}, descend flight level {GetDescentLevel(cruiseFlightLevel)}.\"");
        sb.AppendLine();
        sb.AppendLine("PILOT");
        sb.AppendLine($"\"Descend flight level {GetDescentLevel(cruiseFlightLevel)}, {FormatCallsign(callsign)}.\"");
        sb.AppendLine();
        sb.AppendLine();

        // PHASE 6: HANDOFF TO APPROACH & STAR / ARRIVAL CLEARANCE
        sb.AppendLine("PHASE 6 – HANDOFF TO APPROACH & STAR / ARRIVAL CLEARANCE");
        sb.AppendLine("───────────────────────────────────────────────────────────────");
        sb.AppendLine();
        sb.AppendLine("CENTER");
        sb.AppendLine($"\"{FormatCallsign(callsign)}, contact {GetArrivalName(destinationIcao)} on {GetArrivalFrequency(destinationIcao)}.\"");
        sb.AppendLine();
        sb.AppendLine("PILOT");
        sb.AppendLine($"\"{GetArrivalFrequency(destinationIcao)}, {FormatCallsign(callsign)}.\"");
        sb.AppendLine();
        sb.AppendLine("PILOT → ARRIVAL");
        var entryFix = starResult.Mode == ProcedureSelectionMode.Published && starResult.MatchingEntryFix is not null
            ? starResult.MatchingEntryFix
            : "inbound";
        sb.AppendLine($"\"{GetArrivalName(destinationIcao)} Arrival, {FormatCallsign(callsign)}, descending flight level {GetDescentLevel(cruiseFlightLevel)} to [LOWER ALT], information [INFO], inbound {entryFix}.\"");
        sb.AppendLine();
        sb.AppendLine("ARRIVAL");
        sb.AppendLine($"\"{FormatCallsign(callsign)}, {GetArrivalName(destinationIcao)} Arrival, radar contact.");
        if (starResult.Mode == ProcedureSelectionMode.Published && starResult.SelectedStar is not null)
        {
            sb.AppendLine($"Continue to {starResult.MatchingEntryFix ?? "STAR entry"}, descend to [ALTITUDE], QNH [QNH].\"");
        }
        else
        {
            sb.AppendLine($"Expect vectors for arrival, descend to [ALTITUDE], QNH [QNH].\"");
        }
        sb.AppendLine();
        sb.AppendLine("PILOT");
        if (starResult.Mode == ProcedureSelectionMode.Published && starResult.SelectedStar is not null)
        {
            sb.AppendLine($"\"Continue to {starResult.MatchingEntryFix ?? "STAR entry"}, descend [ALTITUDE], QNH [QNH], {FormatCallsign(callsign)}.\"");
        }
        else
        {
            sb.AppendLine($"\"Expect vectors, descend [ALTITUDE], QNH [QNH], {FormatCallsign(callsign)}.\"");
        }
        sb.AppendLine();
        
        if (approachResult.Mode == ProcedureSelectionMode.Published && approachResult.SelectedApproach is not null)
        {
            sb.AppendLine("(Approaching approach entry)");
            sb.AppendLine();
            sb.AppendLine("ARRIVAL");
            var approachClearance = GenerateApproachClearance(callsign, approachResult, arrivalRunway, starResult);
            sb.AppendLine(approachClearance);
            sb.AppendLine();
            sb.AppendLine("PILOT");
            sb.AppendLine($"\"Cleared {approachName} runway {arrivalRunway.RunwayIdentifier} [via procedure], will report runway in sight, {FormatCallsign(callsign)}.\"");
            sb.AppendLine();
        }
        sb.AppendLine();

        // PHASE 7: FINAL APPROACH & TOWER
        sb.AppendLine("PHASE 7 – FINAL APPROACH & TOWER");
        sb.AppendLine("───────────────────────────────────────────────────────────────");
        sb.AppendLine();
        sb.AppendLine("(Descending on approach)");
        sb.AppendLine();
        sb.AppendLine("PILOT → ARRIVAL");
        sb.AppendLine($"\"{GetArrivalName(destinationIcao)} Arrival, {FormatCallsign(callsign)}, runway in sight.\"");
        sb.AppendLine();
        sb.AppendLine("ARRIVAL");
        sb.AppendLine($"\"{FormatCallsign(callsign)}, roger. Contact {GetTowerName(destinationIcao)} Tower on {GetTowerFrequency(destinationIcao)}.\"");
        sb.AppendLine();
        sb.AppendLine("PILOT");
        sb.AppendLine($"\"{GetTowerFrequency(destinationIcao)}, {FormatCallsign(callsign)}.\"");
        sb.AppendLine();
        sb.AppendLine("PILOT → TOWER");
        sb.AppendLine($"\"{GetTowerName(destinationIcao)} Tower, {FormatCallsign(callsign)}, {approachName} runway {arrivalRunway.RunwayIdentifier}, passing [FIX].\"");
        sb.AppendLine();
        sb.AppendLine("TOWER");
        sb.AppendLine($"\"{FormatCallsign(callsign)}, {GetTowerName(destinationIcao)} Tower, wind {destinationWeather.WindDirectionDegrees:D3} at {destinationWeather.WindSpeedKnots}, runway {arrivalRunway.RunwayIdentifier}, continue approach, number one.\"");
        sb.AppendLine();
        sb.AppendLine("PILOT");
        sb.AppendLine($"\"Continue approach, number one, {FormatCallsign(callsign)}.\"");
        sb.AppendLine();
        sb.AppendLine("(On short final)");
        sb.AppendLine();
        sb.AppendLine("TOWER");
        sb.AppendLine($"\"{FormatCallsign(callsign)}, runway {arrivalRunway.RunwayIdentifier}, cleared to land.\"");
        sb.AppendLine();
        sb.AppendLine("PILOT");
        sb.AppendLine($"\"Cleared to land, runway {arrivalRunway.RunwayIdentifier}, {FormatCallsign(callsign)}.\"");
        sb.AppendLine();
        sb.AppendLine();

        // PHASE 8: ROLLOUT, VACATE & TAXI IN
        sb.AppendLine("PHASE 8 – ROLLOUT, VACATE & TAXI IN");
        sb.AppendLine("───────────────────────────────────────────────────────────────");
        sb.AppendLine();
        sb.AppendLine("(After landing)");
        sb.AppendLine();
        sb.AppendLine("TOWER");
        sb.AppendLine($"\"{FormatCallsign(callsign)}, vacate [LEFT/RIGHT] when able, contact Ground on {GetGroundFrequency(destinationIcao)}.\"");
        sb.AppendLine();
        sb.AppendLine("PILOT");
        sb.AppendLine($"\"Vacate [LEFT/RIGHT], then Ground {GetGroundFrequency(destinationIcao)}, {FormatCallsign(callsign)}.\"");
        sb.AppendLine();
        sb.AppendLine("PILOT → GROUND");
        sb.AppendLine($"\"{GetGroundName(destinationIcao)} Ground, {FormatCallsign(callsign)}, vacated runway {arrivalRunway.RunwayIdentifier} at [EXIT], request taxi to stand.\"");
        sb.AppendLine();
        sb.AppendLine("GROUND");
        sb.AppendLine($"\"{FormatCallsign(callsign)}, taxi to stand [STAND] via [TAXI ROUTE].\"");
        sb.AppendLine();
        sb.AppendLine("PILOT");
        sb.AppendLine($"\"Taxi to stand [STAND] via [TAXI ROUTE], {FormatCallsign(callsign)}.\"");
        sb.AppendLine();
        sb.AppendLine("(Parked)");
        sb.AppendLine();
        sb.AppendLine("PILOT → GROUND");
        sb.AppendLine($"\"{GetGroundName(destinationIcao)} Ground, {FormatCallsign(callsign)}, stand [STAND], request shutdown.\"");
        sb.AppendLine();
        sb.AppendLine("GROUND");
        sb.AppendLine($"\"{FormatCallsign(callsign)}, shutdown approved, good day.\"");
        sb.AppendLine();
        sb.AppendLine("PILOT");
        sb.AppendLine($"\"Shutdown approved, good day, {FormatCallsign(callsign)}.\"");
        sb.AppendLine();
        sb.AppendLine("═══════════════════════════════════════════════════════════════");
        sb.AppendLine("                    END OF ATC SERVICE");
        sb.AppendLine("═══════════════════════════════════════════════════════════════");

        return sb.ToString();
    }

    private string GenerateClearanceDelivery(
        string callsign,
        string originIcao,
        string destinationIcao,
        NavRunwaySummary departureRunway,
        SidSelectionResult sidResult,
        EnrouteRoute? enrouteRoute,
        int cruiseFlightLevel)
    {
        var sb = new StringBuilder();
        sb.Append($"\"{FormatCallsign(callsign)}, {GetClearanceDeliveryName(originIcao)} Clearance, cleared to {destinationIcao} airport");

        // SID
        if (sidResult.Mode == ProcedureSelectionMode.Published && sidResult.SelectedSid is not null)
        {
            sb.Append($" via {sidResult.SelectedSid.ProcedureIdentifier} departure");
            if (!string.IsNullOrWhiteSpace(sidResult.MatchingExitFix))
            {
                sb.Append($", {sidResult.MatchingExitFix} transition");
            }
        }
        else
        {
            sb.Append(" via radar vectors");
        }

        // Enroute
        sb.Append(", then as filed");

        sb.AppendLine(".");
        sb.AppendLine($"Departure runway {departureRunway.RunwayIdentifier}, initial climb {GetInitialAltitude(cruiseFlightLevel)}, squawk {GenerateSquawk()}, expect flight level {cruiseFlightLevel} ten minutes after departure.\"");

        return sb.ToString();
    }

    private string GenerateApproachClearance(
        string callsign,
        ApproachSelectionResult approachResult,
        NavRunwaySummary arrivalRunway,
        StarSelectionResult starResult)
    {
        var sb = new StringBuilder();
        sb.Append($"\"{FormatCallsign(callsign)}, ");

        if (approachResult.Mode == ProcedureSelectionMode.Published && approachResult.SelectedApproach is not null)
        {
            var approach = approachResult.SelectedApproach;
            var starExit = starResult.Mode == ProcedureSelectionMode.Published && starResult.SelectedStar is not null
                ? starResult.SelectedStar.ExitFixIdentifier
                : null;

            if (!string.IsNullOrWhiteSpace(starExit) && !string.IsNullOrWhiteSpace(approach.InitialApproachFixIdentifier))
            {
                sb.Append($"from {starExit} cleared {approach.ApproachTypeCode} {approach.ProcedureIdentifier} approach runway {arrivalRunway.RunwayIdentifier}");
            }
            else
            {
                sb.Append($"cleared {approach.ApproachTypeCode} {approach.ProcedureIdentifier} approach runway {arrivalRunway.RunwayIdentifier}");
            }

            // Add altitude constraints if available
            if (!string.IsNullOrWhiteSpace(approach.InitialApproachFixIdentifier))
            {
                sb.Append($", proceed via published procedure.");
            }
        }
        else
        {
            sb.Append($"cleared visual approach runway {arrivalRunway.RunwayIdentifier}");
        }

        sb.Append(". Report runway in sight.\"");

        return sb.ToString();
    }

    private string GetReadback(
        string callsign,
        NavRunwaySummary departureRunway,
        SidSelectionResult sidResult,
        int cruiseFlightLevel)
    {
        var sidName = sidResult.Mode == ProcedureSelectionMode.Published && sidResult.SelectedSid is not null
            ? sidResult.SelectedSid.ProcedureIdentifier
            : "vectors";
        return $"Cleared to [DEST] via {sidName} departure, [TRANSITION], then as filed. Departure runway {departureRunway.RunwayIdentifier}, initial climb {GetInitialAltitude(cruiseFlightLevel)}, squawk [SQUAWK], expect flight level {cruiseFlightLevel}";
    }

    private string FormatCallsign(string callsign)
    {
        // Format callsign with spaces (e.g., "AeroAI 123" -> "AeroAI one two three")
        var parts = callsign.Split(' ');
        if (parts.Length > 1)
        {
            var letters = parts[0];
            var numbers = parts[1];
            var numberWords = string.Join(" ", numbers.Select(c => c switch
            {
                '0' => "zero", '1' => "one", '2' => "two", '3' => "three", '4' => "four",
                '5' => "five", '6' => "six", '7' => "seven", '8' => "eight", '9' => "nine",
                _ => c.ToString()
            }));
            return $"{letters} {numberWords}";
        }
        return callsign;
    }

    private string GenerateSquawk() => $"{_random.Next(1, 8)}{_random.Next(0, 8)}{_random.Next(0, 8)}{_random.Next(0, 8)}";

    private string GetInitialAltitude(int cruiseFl) => cruiseFl > 300 ? "five thousand feet" : "three thousand feet";
    private string GetIntermediateAltitude(int cruiseFl) => cruiseFl > 300 ? "180" : "120";
    private string GetDescentLevel(int cruiseFl) => cruiseFl > 300 ? "180" : "120";

    private string GetPushDirection(NavRunwaySummary runway)
    {
        var heading = runway.TrueHeadingDegrees;
        if (heading >= 315 || heading < 45) return "north";
        if (heading >= 45 && heading < 135) return "east";
        if (heading >= 135 && heading < 225) return "south";
        return "west";
    }

    private string GetClearanceDeliveryName(string icao) => icao.StartsWith("ED") ? "Munich" : GetAirportName(icao);
    private string GetGroundName(string icao) => icao.StartsWith("ED") ? "Munich" : GetAirportName(icao);
    private string GetTowerName(string icao) => icao.StartsWith("ED") ? "Munich" : GetAirportName(icao);
    private string GetDepartureName(string icao) => icao.StartsWith("ED") ? "Munich" : GetAirportName(icao);
    private string GetArrivalName(string icao) => icao.StartsWith("LO") ? "Innsbruck" : GetAirportName(icao);

    private string GetAirportName(string icao)
    {
        return icao switch
        {
            "EDDM" => "Munich",
            "LOWI" => "Innsbruck",
            "KJFK" => "New York",
            "KLAX" => "Los Angeles",
            _ => icao
        };
    }

    private string GetDepartureFrequency(string icao) => "119.2";
    private string GetArrivalFrequency(string icao) => "120.1";
    private string GetTowerFrequency(string icao) => "118.1";
    private string GetGroundFrequency(string icao) => "121.7";
}

