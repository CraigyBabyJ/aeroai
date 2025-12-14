# AeroAI ATC Decision-Making Logic Layer - Complete Design

## Overview

This document defines the complete architecture, interfaces, classes, and algorithms for AeroAI's ATC decision-making system. The system dynamically selects runways, SIDs, STARs, and approaches based on weather, aircraft performance, and enroute routing—ignoring SimBrief's procedure selections.

**Key Principle**: SimBrief route is used **ONLY** for enroute waypoints. All terminal procedures (runways, SIDs, STARs, approaches) are determined by AeroAI using realistic ATC logic.

---

## Architecture

### Core Components

```
┌─────────────────────────────────────────────────────────────┐
│                    ATC Decision Engine                       │
├─────────────────────────────────────────────────────────────┤
│                                                              │
│  ┌──────────────────┐         ┌──────────────────┐          │
│  │ IRunwaySelector  │         │IProcedureSelector│          │
│  │                  │         │                  │          │
│  │ - Departure      │         │ - SID Selection  │          │
│  │ - Arrival        │         │ - STAR Selection │          │
│  └──────────────────┘         │ - Approach       │          │
│           │                   └──────────────────┘          │
│           │                            │                    │
│           ▼                            ▼                    │
│  ┌──────────────────┐         ┌──────────────────┐          │
│  │ RunwaySelector   │         │ ProcedureSelector│          │
│  └──────────────────┘         └──────────────────┘          │
│                                                              │
└─────────────────────────────────────────────────────────────┘
           │                              │
           ▼                              ▼
┌──────────────────┐         ┌──────────────────┐
│  NavDataReader   │         │  Weather Service │
│  (SQLite)        │         │  (External)      │
└──────────────────┘         └──────────────────┘
```

---

## Data Models

### Existing Models (Reference)

All models are in `AeroAI.Models` namespace:

- `NavRunwaySummary` - Runway metadata (heading, length, ILS/RNAV flags)
- `WeatherInfo` - Wind, visibility, ceiling, IFR/VFR flags
- `AircraftPerformanceProfile` - Required distances, wind limits
- `EnrouteRoute` - Ordered waypoint list from SimBrief (enroute only)
- `SidSummary` - SID metadata with exit fix
- `StarSummary` - STAR metadata with entry/exit fixes
- `ApproachSummary` - Approach metadata (type, precision, IAF/FAF)
- `RunwaySelectionResult` - Runway selection outcome
- `SidSelectionResult` - SID selection outcome (or vectors)
- `StarSelectionResult` - STAR selection outcome (or vectors)
- `ApproachSelectionResult` - Approach selection outcome (or vectors)
- `ProcedureSelectionMode` - Enum: None, Published, Vectors

---

## Interface Definitions

### IRunwaySelector

```csharp
namespace AeroAI.Logic;

/// <summary>
/// Selects departure and arrival runways based on wind, aircraft performance,
/// and instrument approach availability.
/// </summary>
public interface IRunwaySelector
{
    /// <summary>
    /// Selects the optimal departure runway for the given airport.
    /// </summary>
    /// <param name="airportIcao">Airport ICAO code (e.g., "KJFK")</param>
    /// <param name="weather">Current weather conditions at departure airport</param>
    /// <param name="aircraft">Aircraft performance profile (weight, limits)</param>
    /// <param name="availableRunways">All runways available at this airport</param>
    /// <returns>Selected runway with reasoning and candidate list</returns>
    RunwaySelectionResult SelectDepartureRunway(
        string airportIcao,
        WeatherInfo weather,
        AircraftPerformanceProfile aircraft,
        IReadOnlyList<NavRunwaySummary> availableRunways);

    /// <summary>
    /// Selects the optimal arrival runway for the given airport.
    /// Prioritizes precision approaches in IMC conditions.
    /// </summary>
    /// <param name="airportIcao">Airport ICAO code</param>
    /// <param name="weather">Current weather conditions at arrival airport</param>
    /// <param name="aircraft">Aircraft performance profile</param>
    /// <param name="availableRunways">All runways available at this airport</param>
    /// <returns>Selected runway with reasoning and candidate list</returns>
    RunwaySelectionResult SelectArrivalRunway(
        string airportIcao,
        WeatherInfo weather,
        AircraftPerformanceProfile aircraft,
        IReadOnlyList<NavRunwaySummary> availableRunways);
}
```

### IProcedureSelector

```csharp
namespace AeroAI.Logic;

/// <summary>
/// Selects SIDs, STARs, and approaches based on enroute routing and runway assignments.
/// </summary>
public interface IProcedureSelector
{
    /// <summary>
    /// Selects a SID that best matches the enroute route's initial waypoints.
    /// Returns vectors if no suitable SID is found.
    /// </summary>
    /// <param name="airportIcao">Departure airport ICAO</param>
    /// <param name="departureRunway">Selected departure runway</param>
    /// <param name="route">Enroute route (waypoints from SimBrief)</param>
    /// <param name="availableSids">All SIDs available for this airport/runway</param>
    /// <returns>SID selection result (published SID or vectors)</returns>
    SidSelectionResult SelectSidForRoute(
        string airportIcao,
        NavRunwaySummary departureRunway,
        EnrouteRoute route,
        IReadOnlyList<SidSummary> availableSids);

    /// <summary>
    /// Selects a STAR that best matches the enroute route's final waypoints.
    /// Returns vectors if no suitable STAR is found.
    /// </summary>
    /// <param name="airportIcao">Arrival airport ICAO</param>
    /// <param name="arrivalRunway">Selected arrival runway</param>
    /// <param name="route">Enroute route (waypoints from SimBrief)</param>
    /// <param name="availableStars">All STARs available for this airport/runway</param>
    /// <returns>STAR selection result (published STAR or vectors)</returns>
    StarSelectionResult SelectStarForRoute(
        string airportIcao,
        NavRunwaySummary arrivalRunway,
        EnrouteRoute route,
        IReadOnlyList<StarSummary> availableStars);

    /// <summary>
    /// Selects an approach procedure for the arrival runway.
    /// Considers weather (ILS in IMC), STAR connectivity, and runway alignment.
    /// </summary>
    /// <param name="airportIcao">Arrival airport ICAO</param>
    /// <param name="arrivalRunway">Selected arrival runway</param>
    /// <param name="weather">Current weather at arrival airport</param>
    /// <param name="availableApproaches">All approaches for this runway</param>
    /// <param name="starSelection">Previously selected STAR (for connectivity check)</param>
    /// <returns>Approach selection result (published approach or vectors)</returns>
    ApproachSelectionResult SelectApproachForRunway(
        string airportIcao,
        NavRunwaySummary arrivalRunway,
        WeatherInfo weather,
        IReadOnlyList<ApproachSummary> availableApproaches,
        StarSelectionResult? starSelection);
}
```

---

## Concrete Class: RunwaySelector

### Class Structure

```csharp
namespace AeroAI.Logic;

/// <summary>
/// Implements runway selection logic using wind analysis, aircraft performance,
/// and instrument approach availability.
/// </summary>
public sealed class RunwaySelector : IRunwaySelector
{
    // Constants
    private const double RequiredLengthMarginFactor = 1.3;  // 30% safety margin
    private const double MaxTailwindKnots = 10.0;           // Hard limit (override aircraft if needed)
    private const double MaxCrosswindKnots = 35.0;          // Hard limit (override aircraft if needed)
    private const int MinRunwayLengthFeet = 3000;          // Absolute minimum (safety)

    // Scoring weights (tunable)
    private const double HeadwindScoreMultiplier = 2.0;
    private const double CrosswindPenaltyMultiplier = 0.5;
    private const double LengthScoreDivisor = 100.0;
    private const double MaxLengthScore = 50.0;
    private const double IlsBonusScore = 40.0;
    private const double PreferredRunwayBonus = 10.0;

    // Public methods (from interface)
    public RunwaySelectionResult SelectDepartureRunway(...) { ... }
    public RunwaySelectionResult SelectArrivalRunway(...) { ... }

    // Private helper methods
    private List<ScoredRunway> FilterAndScoreRunways(...) { ... }
    private NavRunwaySummary? ChooseFallbackRunway(...) { ... }
    private static (double Headwind, double Crosswind) ComputeWindComponents(...) { ... }
    private static int NormalizeAngleDegrees(int angle) { ... }
    private static bool IsImcConditions(WeatherInfo weather) { ... }

    // Internal scoring class
    private sealed class ScoredRunway
    {
        public required NavRunwaySummary Runway { get; init; }
        public required double Score { get; init; }
        public required double HeadwindKnots { get; init; }
        public required double TailwindKnots { get; init; }
        public required double CrosswindKnots { get; init; }
        public required string RejectionReason { get; init; } = string.Empty;
    }
}
```

---

## Algorithm Pseudocode: Runway Selection

### SelectDepartureRunway()

```
FUNCTION SelectDepartureRunway(airportIcao, weather, aircraft, availableRunways):
    
    // Step 1: Filter and score all runways
    candidates = FilterAndScoreRunways(
        availableRunways,
        weather,
        aircraft,
        isDeparture = TRUE,
        out scoredRunways
    )
    
    // Step 2: If no candidates passed filters, use fallback
    IF candidates.Count == 0:
        fallback = ChooseFallbackRunway(availableRunways, weather, aircraft)
        RETURN RunwaySelectionResult {
            SelectedRunway = fallback,
            CandidatesConsidered = availableRunways,
            Reason = "No runway met strict criteria; chose fallback by length/tailwind"
        }
    
    // Step 3: Select highest-scoring candidate
    best = candidates.OrderByDescending(c => c.Score).First()
    
    RETURN RunwaySelectionResult {
        SelectedRunway = best.Runway,
        CandidatesConsidered = scoredRunways,
        Reason = "Selected based on headwind, length, and departure preferences"
    }
END FUNCTION
```

### SelectArrivalRunway()

```
FUNCTION SelectArrivalRunway(airportIcao, weather, aircraft, availableRunways):
    
    // Same structure as departure, but with IMC precision approach requirement
    candidates = FilterAndScoreRunways(
        availableRunways,
        weather,
        aircraft,
        isDeparture = FALSE,
        out scoredRunways
    )
    
    IF candidates.Count == 0:
        fallback = ChooseFallbackRunway(availableRunways, weather, aircraft)
        RETURN RunwaySelectionResult {
            SelectedRunway = fallback,
            CandidatesConsidered = availableRunways,
            Reason = "No runway met strict criteria; chose fallback with approach availability"
        }
    
    best = candidates.OrderByDescending(c => c.Score).First()
    
    RETURN RunwaySelectionResult {
        SelectedRunway = best.Runway,
        CandidatesConsidered = scoredRunways,
        Reason = "Selected based on headwind, length, and precision approach availability"
    }
END FUNCTION
```

### FilterAndScoreRunways() - Core Algorithm

```
FUNCTION FilterAndScoreRunways(
    availableRunways,
    weather,
    aircraft,
    isDeparture,
    out scoredRunways
):
    
    candidates = NEW List<ScoredRunway>()
    isImc = IsImcConditions(weather)
    
    FOR EACH runway IN availableRunways:
        
        // === STEP 1: Compute Wind Components ===
        (headwind, crosswind) = ComputeWindComponents(
            runway.TrueHeadingDegrees,
            weather.WindDirectionDegrees,
            weather.WindSpeedKnots
        )
        
        tailwind = MAX(0, -headwind)  // Positive tailwind value
        absCrosswind = ABS(crosswind)
        
        // === STEP 2: Apply Hard Filters (Reject if fails) ===
        
        // Filter: Excessive tailwind
        IF tailwind > aircraft.MaxTailwindComponentKnots:
            CONTINUE  // Skip this runway
        
        // Filter: Excessive crosswind
        IF absCrosswind > aircraft.MaxCrosswindComponentKnots:
            CONTINUE
        
        // Filter: Runway too short
        requiredLength = IF isDeparture THEN 
            aircraft.RequiredTakeoffDistanceFeet 
        ELSE 
            aircraft.RequiredLandingDistanceFeet
        
        minimumAcceptable = requiredLength * RequiredLengthMarginFactor
        
        IF runway.LengthFeet < minimumAcceptable:
            CONTINUE
        
        // Filter: Absolute minimum length (safety)
        IF runway.LengthFeet < MinRunwayLengthFeet:
            CONTINUE
        
        // Filter: Arrival in IMC requires instrument approach
        IF NOT isDeparture AND isImc:
            IF NOT runway.HasIlsOrLocalizer AND NOT runway.HasRnavApproach:
                CONTINUE
        
        // === STEP 3: Score the Runway ===
        score = 0.0
        
        // Favor headwind (strong positive contribution)
        score += headwind * HeadwindScoreMultiplier
        
        // Penalize crosswind (slight negative)
        score -= absCrosswind * CrosswindPenaltyMultiplier
        
        // Prefer longer runways (capped effect)
        lengthScore = MIN(runway.LengthFeet / LengthScoreDivisor, MaxLengthScore)
        score += lengthScore
        
        // Bonus: Precision approach in IMC (arrival only)
        IF NOT isDeparture AND isImc AND runway.HasIlsOrLocalizer:
            score += IlsBonusScore
        
        // Bonus: Airport configuration preferences
        IF isDeparture AND runway.IsPreferredDeparture:
            score += PreferredRunwayBonus
        
        IF NOT isDeparture AND runway.IsPreferredArrival:
            score += PreferredRunwayBonus
        
        // Add to candidates
        candidates.ADD(ScoredRunway {
            Runway = runway,
            Score = score,
            HeadwindKnots = headwind,
            TailwindKnots = tailwind,
            CrosswindKnots = absCrosswind
        })
    END FOR
    
    scoredRunways = candidates.Select(c => c.Runway).ToArray()
    RETURN candidates
    
END FUNCTION
```

### ComputeWindComponents() - Wind Analysis

```
FUNCTION ComputeWindComponents(
    runwayHeadingDeg: INTEGER,      // 0-359
    windDirectionDeg: INTEGER,      // 0-359 (direction wind is FROM)
    windSpeedKnots: INTEGER
) RETURNS (headwind: DOUBLE, crosswind: DOUBLE):
    
    // Handle calm winds
    IF windSpeedKnots <= 1:
        RETURN (0.0, 0.0)
    
    // Normalize angles to [0, 360)
    rw = NormalizeAngleDegrees(runwayHeadingDeg)
    wd = NormalizeAngleDegrees(windDirectionDeg)
    
    // Compute relative angle: wind direction relative to runway heading
    // If wind is FROM 270° and runway is 090°, relative = 270 - 90 = 180° (direct tailwind)
    relativeAngle = NormalizeAngleDegrees(wd - rw)
    
    // Convert to radians
    relativeRad = relativeAngle * PI / 180.0
    
    // Compute components using trigonometry
    // Headwind = wind speed * cos(relative angle)
    // Crosswind = wind speed * sin(relative angle)
    headwind = windSpeedKnots * COS(relativeRad)
    crosswind = windSpeedKnots * SIN(relativeRad)
    
    // Note: headwind positive = favorable, negative = tailwind
    // crosswind positive = right crosswind, negative = left crosswind
    
    RETURN (headwind, crosswind)
    
END FUNCTION
```

### NormalizeAngleDegrees()

```
FUNCTION NormalizeAngleDegrees(angle: INTEGER) RETURNS INTEGER:
    result = angle MOD 360
    IF result < 0:
        result = result + 360
    RETURN result
END FUNCTION
```

### ChooseFallbackRunway()

```
FUNCTION ChooseFallbackRunway(
    availableRunways,
    weather,
    aircraft
) RETURNS NavRunwaySummary?:
    
    IF availableRunways.Count == 0:
        RETURN NULL
    
    scored = NEW List<(Runway, Tailwind, Crosswind)>()
    
    FOR EACH runway IN availableRunways:
        (headwind, crosswind) = ComputeWindComponents(
            runway.TrueHeadingDegrees,
            weather.WindDirectionDegrees,
            weather.WindSpeedKnots
        )
        
        tailwind = MAX(0, -headwind)
        absCrosswind = ABS(crosswind)
        
        scored.ADD((runway, tailwind, absCrosswind))
    END FOR
    
    // Choose runway with minimal tailwind, then maximum length
    best = scored
        .OrderBy(s => s.Tailwind)           // Minimize tailwind
        .ThenByDescending(s => s.Runway.LengthFeet)  // Maximize length
        .First()
    
    RETURN best.Runway
    
END FUNCTION
```

### IsImcConditions()

```
FUNCTION IsImcConditions(weather: WeatherInfo) RETURNS BOOLEAN:
    
    // Use explicit IFR flag if available
    IF weather.IsIfr:
        RETURN TRUE
    
    // Check ceiling (low ceiling = IMC)
    IF weather.CeilingFeet > 0 AND weather.CeilingFeet < 1000:
        RETURN TRUE
    
    // Check visibility (low visibility = IMC)
    // 4800 meters ≈ 3 statute miles (typical VFR minimum)
    IF weather.VisibilityMeters > 0 AND weather.VisibilityMeters < 4800:
        RETURN TRUE
    
    RETURN FALSE
    
END FUNCTION
```

---

## Concrete Class: ProcedureSelector

### Class Structure

```csharp
namespace AeroAI.Logic;

/// <summary>
/// Implements procedure selection logic (SIDs, STARs, approaches) based on
/// enroute routing and runway assignments.
/// </summary>
public sealed class ProcedureSelector : IProcedureSelector
{
    // Constants
    private const int MaxSidMatchDistance = 5;      // Max waypoint index for SID exit match
    private const int MaxStarMatchDistance = 5;     // Max waypoints from end for STAR entry match
    private const double SidMatchScoreBase = 100.0;
    private const double SidMatchScorePenalty = 10.0;  // Per waypoint distance
    private const double StarMatchScoreBase = 100.0;
    private const double StarMatchScorePenalty = 10.0;
    
    // Approach scoring weights
    private const double IlsImcScore = 100.0;
    private const double RnavImcScore = 60.0;
    private const double NonPrecisionImcPenalty = -50.0;
    private const double IlsVmcScore = 60.0;
    private const double RnavVmcScore = 50.0;
    private const double StarConnectivityBonus = 40.0;
    private const double StraightInBonus = 20.0;
    private const double CirclingOnlyPenalty = -20.0;

    // Public methods (from interface)
    public SidSelectionResult SelectSidForRoute(...) { ... }
    public StarSelectionResult SelectStarForRoute(...) { ... }
    public ApproachSelectionResult SelectApproachForRunway(...) { ... }

    // Private helper methods
    private static bool IsImc(WeatherInfo weather) { ... }
    private static int IndexOfIgnoreCase(IReadOnlyList<string> list, string value) { ... }
    private static int LastIndexOfIgnoreCase(IReadOnlyList<string> list, string value) { ... }
    private static bool WaypointMatches(string waypoint1, string waypoint2) { ... }
}
```

---

## Algorithm Pseudocode: SID Selection

### SelectSidForRoute()

```
FUNCTION SelectSidForRoute(
    airportIcao,
    departureRunway,
    route,
    availableSids
) RETURNS SidSelectionResult:
    
    // === STEP 1: Filter SIDs for this runway ===
    runwaySids = availableSids
        .Where(s => s.AirportIcao == airportIcao)
        .Where(s => 
            s.RunwayIdentifier IS EMPTY OR
            s.RunwayIdentifier == departureRunway.RunwayIdentifier OR
            s.RunwayIdentifier == "ALL"
        )
        .ToList()
    
    // === STEP 2: Check if any SIDs exist ===
    IF runwaySids.Count == 0:
        RETURN SidSelectionResult {
            Mode = Vectors,
            SelectedSid = NULL,
            MatchingExitFix = NULL,
            Reason = "No SIDs available for departure runway; assigning vectors departure"
        }
    
    // === STEP 3: Check if enroute route has waypoints ===
    IF route.WaypointIdentifiers.Count == 0:
        RETURN SidSelectionResult {
            Mode = Vectors,
            SelectedSid = NULL,
            MatchingExitFix = NULL,
            Reason = "No enroute waypoints available; assigning vectors departure"
        }
    
    // === STEP 4: Match SID exit fixes to enroute waypoints ===
    bestScore = -INFINITY
    bestSid = NULL
    bestMatchFix = NULL
    
    FOR EACH sid IN runwaySids:
        exitFix = sid.ExitFixIdentifier
        
        IF exitFix IS EMPTY:
            CONTINUE
        
        // Find exit fix in enroute route
        index = IndexOfIgnoreCase(route.WaypointIdentifiers, exitFix)
        
        IF index < 0:
            CONTINUE  // Exit fix not found in route
        
        // Only consider exit fixes that appear early in route
        maxIndex = MIN(5, route.WaypointIdentifiers.Count - 1)
        
        IF index > maxIndex:
            CONTINUE  // Exit fix too far into route
        
        // Score: closer to start = higher score
        score = SidMatchScoreBase - (index * SidMatchScorePenalty)
        
        IF score > bestScore:
            bestScore = score
            bestSid = sid
            bestMatchFix = route.WaypointIdentifiers[index]
    END FOR
    
    // === STEP 5: Return result ===
    IF bestSid IS NULL:
        RETURN SidSelectionResult {
            Mode = Vectors,
            SelectedSid = NULL,
            MatchingExitFix = NULL,
            Reason = "No SID exit fix matched early enroute waypoints; assigning vectors departure"
        }
    
    RETURN SidSelectionResult {
        Mode = Published,
        SelectedSid = bestSid,
        MatchingExitFix = bestMatchFix,
        Reason = "Selected SID {bestSid.ProcedureIdentifier} matching enroute route via exit fix {bestMatchFix}"
    }
    
END FUNCTION
```

---

## Algorithm Pseudocode: STAR Selection

### SelectStarForRoute()

```
FUNCTION SelectStarForRoute(
    airportIcao,
    arrivalRunway,
    route,
    availableStars
) RETURNS StarSelectionResult:
    
    // === STEP 1: Filter STARs for this runway ===
    runwayStars = availableStars
        .Where(s => s.AirportIcao == airportIcao)
        .Where(s => 
            s.RunwayIdentifier IS EMPTY OR
            s.RunwayIdentifier == arrivalRunway.RunwayIdentifier OR
            s.RunwayIdentifier == "ALL"
        )
        .ToList()
    
    // === STEP 2: Check if any STARs exist ===
    IF runwayStars.Count == 0:
        RETURN StarSelectionResult {
            Mode = Vectors,
            SelectedStar = NULL,
            MatchingEntryFix = NULL,
            Reason = "No STARs available for arrival runway; assigning vectors arrival"
        }
    
    // === STEP 3: Check if enroute route has waypoints ===
    IF route.WaypointIdentifiers.Count == 0:
        RETURN StarSelectionResult {
            Mode = Vectors,
            SelectedStar = NULL,
            MatchingEntryFix = NULL,
            Reason = "No enroute waypoints available; assigning vectors arrival"
        }
    
    // === STEP 4: Match STAR entry fixes to enroute waypoints ===
    bestScore = -INFINITY
    bestStar = NULL
    bestMatchFix = NULL
    
    FOR EACH star IN runwayStars:
        entryFix = star.EntryFixIdentifier
        
        IF entryFix IS EMPTY:
            CONTINUE
        
        // Find entry fix in enroute route (search from end)
        index = LastIndexOfIgnoreCase(route.WaypointIdentifiers, entryFix)
        
        IF index < 0:
            CONTINUE  // Entry fix not found in route
        
        // Only consider entry fixes near end of route
        lastIndex = route.WaypointIdentifiers.Count - 1
        distanceFromEnd = lastIndex - index
        
        IF distanceFromEnd > MaxStarMatchDistance:
            CONTINUE  // Entry fix too far from end
        
        // Score: closer to end = higher score
        score = StarMatchScoreBase - (distanceFromEnd * StarMatchScorePenalty)
        
        IF score > bestScore:
            bestScore = score
            bestStar = star
            bestMatchFix = route.WaypointIdentifiers[index]
    END FOR
    
    // === STEP 5: Return result ===
    IF bestStar IS NULL:
        RETURN StarSelectionResult {
            Mode = Vectors,
            SelectedStar = NULL,
            MatchingEntryFix = NULL,
            Reason = "No STAR entry fix matched final enroute waypoints; assigning vectors arrival"
        }
    
    RETURN StarSelectionResult {
        Mode = Published,
        SelectedStar = bestStar,
        MatchingEntryFix = bestMatchFix,
        Reason = "Selected STAR {bestStar.ProcedureIdentifier} matching enroute route via entry fix {bestMatchFix}"
    }
    
END FUNCTION
```

---

## Algorithm Pseudocode: Approach Selection

### SelectApproachForRunway()

```
FUNCTION SelectApproachForRunway(
    airportIcao,
    arrivalRunway,
    weather,
    availableApproaches,
    starSelection
) RETURNS ApproachSelectionResult:
    
    // === STEP 1: Filter approaches for this runway ===
    runwayApproaches = availableApproaches
        .Where(a => a.AirportIcao == airportIcao)
        .Where(a => a.RunwayIdentifier == arrivalRunway.RunwayIdentifier)
        .ToList()
    
    // === STEP 2: Check if any approaches exist ===
    IF runwayApproaches.Count == 0:
        RETURN ApproachSelectionResult {
            Mode = Vectors,
            SelectedApproach = NULL,
            Reason = "No published approaches for runway; vectors/visual only"
        }
    
    // === STEP 3: Determine IMC vs VMC ===
    isImc = IsImc(weather)
    
    // === STEP 4: Score each approach ===
    bestScore = -INFINITY
    bestApproach = NULL
    
    FOR EACH approach IN runwayApproaches:
        score = 0.0
        
        // === Weather-based scoring ===
        IF isImc:
            // IMC: Strongly prefer precision approaches
            IF approach.HasGlideslope OR approach.ApproachTypeCode == "I":
                score += IlsImcScore  // ILS/GLS in IMC = best
            ELSE IF approach.IsRnav:
                score += RnavImcScore  // RNAV in IMC = acceptable
            ELSE:
                score += NonPrecisionImcPenalty  // Non-precision in IMC = poor
        
        ELSE:
            // VMC: All approaches acceptable, slight preference for precision
            IF approach.HasGlideslope OR approach.ApproachTypeCode == "I":
                score += IlsVmcScore
            IF approach.IsRnav:
                score += RnavVmcScore
        
        // === Connectivity scoring ===
        // Bonus: STAR exit fix connects to approach IAF
        IF starSelection.Mode == Published AND starSelection.SelectedStar IS NOT NULL:
            starExit = starSelection.SelectedStar.ExitFixIdentifier
            
            IF starExit IS NOT EMPTY:
                IF approach.InitialApproachFixIdentifier == starExit:
                    score += StarConnectivityBonus  // Clean STAR -> IAF handoff
        
        // === Approach type penalties/bonuses ===
        IF isImc AND NOT approach.SupportsStraightIn:
            score -= CirclingOnlyPenalty  // Circling in IMC is difficult
        
        IF approach.IsCirclingOnly:
            score -= CirclingOnlyPenalty
        
        // === Track best approach ===
        IF score > bestScore:
            bestScore = score
            bestApproach = approach
    END FOR
    
    // === STEP 5: Return result ===
    IF bestApproach IS NULL:
        RETURN ApproachSelectionResult {
            Mode = Vectors,
            SelectedApproach = NULL,
            Reason = "Could not score any approach; vectors to runway"
        }
    
    RETURN ApproachSelectionResult {
        Mode = Published,
        SelectedApproach = bestApproach,
        Reason = "Selected approach {bestApproach.ProcedureIdentifier} based on IMC/VMC and STAR connectivity"
    }
    
END FUNCTION
```

### IsImc() - Weather Classification

```
FUNCTION IsImc(weather: WeatherInfo) RETURNS BOOLEAN:
    
    // Use explicit IFR flag
    IF weather.IsIfr:
        RETURN TRUE
    
    // Low ceiling = IMC
    IF weather.CeilingFeet > 0 AND weather.CeilingFeet < 1000:
        RETURN TRUE
    
    // Low visibility = IMC
    // 4800 meters ≈ 3 statute miles
    IF weather.VisibilityMeters > 0 AND weather.VisibilityMeters < 4800:
        RETURN TRUE
    
    RETURN FALSE
    
END FUNCTION
```

### Helper Functions: Waypoint Matching

```
FUNCTION IndexOfIgnoreCase(
    list: IReadOnlyList<string>,
    value: string
) RETURNS INTEGER:
    
    FOR i = 0 TO list.Count - 1:
        IF String.Equals(list[i], value, OrdinalIgnoreCase):
            RETURN i
    END FOR
    
    RETURN -1
    
END FUNCTION

FUNCTION LastIndexOfIgnoreCase(
    list: IReadOnlyList<string>,
    value: string
) RETURNS INTEGER:
    
    FOR i = list.Count - 1 DOWN TO 0:
        IF String.Equals(list[i], value, OrdinalIgnoreCase):
            RETURN i
    END FOR
    
    RETURN -1
    
END FUNCTION

FUNCTION WaypointMatches(
    waypoint1: string,
    waypoint2: string
) RETURNS BOOLEAN:
    
    // Case-insensitive comparison
    RETURN String.Equals(waypoint1, waypoint2, OrdinalIgnoreCase)
    
END FUNCTION
```

---

## Decision Flow: Complete ATC Clearance Generation

### High-Level Orchestration (Pseudocode)

```
FUNCTION GenerateAtcClearance(
    originIcao,
    destinationIcao,
    enrouteRoute,          // From SimBrief (waypoints only)
    originWeather,
    destinationWeather,
    aircraft,
    navDataReader
):
    
    // === STEP 1: Select Departure Runway ===
    originRunways = navDataReader.GetRunways(originIcao)
    runwaySelector = NEW RunwaySelector()
    
    departureRunwayResult = runwaySelector.SelectDepartureRunway(
        originIcao,
        originWeather,
        aircraft,
        originRunways
    )
    
    departureRunway = departureRunwayResult.SelectedRunway
    
    // === STEP 2: Select Departure SID ===
    availableSids = navDataReader.GetSids(originIcao)
    procedureSelector = NEW ProcedureSelector()
    
    sidResult = procedureSelector.SelectSidForRoute(
        originIcao,
        departureRunway,
        enrouteRoute,
        availableSids
    )
    
    // === STEP 3: Select Arrival Runway ===
    destinationRunways = navDataReader.GetRunways(destinationIcao)
    
    arrivalRunwayResult = runwaySelector.SelectArrivalRunway(
        destinationIcao,
        destinationWeather,
        aircraft,
        destinationRunways
    )
    
    arrivalRunway = arrivalRunwayResult.SelectedRunway
    
    // === STEP 4: Select Arrival STAR ===
    availableStars = navDataReader.GetStars(destinationIcao)
    
    starResult = procedureSelector.SelectStarForRoute(
        destinationIcao,
        arrivalRunway,
        enrouteRoute,
        availableStars
    )
    
    // === STEP 5: Select Approach ===
    availableApproaches = navDataReader.GetApproaches(destinationIcao)
    
    approachResult = procedureSelector.SelectApproachForRunway(
        destinationIcao,
        arrivalRunway,
        destinationWeather,
        availableApproaches,
        starResult
    )
    
    // === STEP 6: Build Clearance ===
    clearance = BuildClearanceText(
        departureRunway,
        sidResult,
        enrouteRoute,
        starResult,
        approachResult,
        arrivalRunway
    )
    
    RETURN clearance
    
END FUNCTION
```

---

## Key Design Decisions

### 1. Wind Component Calculation
- **Method**: Trigonometric decomposition (cos/sin)
- **Reference**: Wind direction is FROM (meteorological convention)
- **Output**: Headwind (positive = favorable), crosswind (absolute value used)

### 2. Runway Scoring Strategy
- **Primary factors**: Headwind (2x weight), length (capped), crosswind (penalty)
- **Secondary factors**: ILS availability (IMC only), airport preferences
- **Fallback**: If no runway passes filters, choose by minimal tailwind + max length

### 3. SID/STAR Matching
- **SID**: Match exit fix to **early** enroute waypoints (first 5)
- **STAR**: Match entry fix to **late** enroute waypoints (last 5)
- **Fallback**: Vectors if no match found

### 4. Approach Selection Priority
- **IMC**: ILS/GLS (100 pts) > RNAV (60 pts) > Non-precision (-50 pts)
- **VMC**: All acceptable, slight preference for precision
- **Connectivity**: Bonus if STAR exit = Approach IAF

### 5. IMC vs VMC Classification
- **IMC triggers**: IFR flag, ceiling < 1000 ft, visibility < 4800 m
- **Default**: VMC if conditions not met

---

## Implementation Notes

### Database Queries (Not Implemented Here)

The following queries are assumed to be implemented in `NavDataReader` or `INavDataRepository`:

1. **GetRunways(icao)**: Query `tbl_runways` for airport
2. **GetSids(icao)**: Query `tbl_sids` + `tbl_pathpoints` to find exit fixes
3. **GetStars(icao)**: Query `tbl_stars` + `tbl_pathpoints` to find entry/exit fixes
4. **GetApproaches(icao)**: Query `tbl_iaps` + `tbl_pathpoints` to find IAF/FAF

### Exit Fix Detection (SID)

To find a SID's exit fix:
- Query `tbl_pathpoints` for SID procedure
- Find the leg with highest `seqno` (last leg)
- Extract `waypoint_identifier` as exit fix

### Entry/Exit Fix Detection (STAR)

To find a STAR's entry fix:
- Query `tbl_pathpoints` for STAR procedure
- Find the leg with lowest `seqno` (first leg)
- Extract `waypoint_identifier` as entry fix

To find a STAR's exit fix:
- Query `tbl_pathpoints` for STAR procedure
- Find the leg with highest `seqno` (last leg)
- Extract `waypoint_identifier` as exit fix

### Approach IAF/FAF Detection

To find an approach's IAF:
- Query `tbl_pathpoints` for approach procedure
- Find leg with `path_termination` = "IF" (Initial Fix)
- Extract `waypoint_identifier` as IAF

To find an approach's FAF:
- Query `tbl_pathpoints` for approach procedure
- Find leg with `path_termination` indicating final approach segment
- Extract `waypoint_identifier` as FAF

---

## Testing Considerations

### Unit Test Scenarios

1. **Runway Selection**:
   - Strong headwind vs tailwind
   - IMC arrival requiring ILS
   - Short runway rejection
   - Excessive crosswind rejection

2. **SID Selection**:
   - Perfect exit fix match
   - Exit fix too far into route
   - No matching SID (vectors fallback)

3. **STAR Selection**:
   - Perfect entry fix match
   - Entry fix too early in route
   - No matching STAR (vectors fallback)

4. **Approach Selection**:
   - IMC with ILS available
   - IMC with RNAV only
   - VMC with multiple options
   - STAR connectivity bonus

---

## Summary

This design provides:

✅ **Complete interface definitions** for `IRunwaySelector` and `IProcedureSelector`  
✅ **Detailed method signatures** with parameter documentation  
✅ **Full pseudocode** for all decision-making algorithms  
✅ **Wind calculation** mathematics  
✅ **Scoring strategies** with tunable constants  
✅ **Fallback logic** for edge cases  
✅ **IMC/VMC classification** rules  
✅ **Procedure matching** algorithms (SID/STAR to enroute)  
✅ **Approach selection** priority matrix  

The design is structured to allow **step-by-step implementation** of each component independently.

