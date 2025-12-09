namespace AtcNavDataDemo.SimBrief;

/// <summary>
/// Flight plan data from SimBrief.
/// NOTE: AeroAI IGNORES SimBrief's departure/arrival runway, SID, and STAR selections.
/// We only use SimBrief for: origin/destination ICAO, enroute waypoints, and callsign.
/// Runways and procedures are determined by AeroAI based on weather and navdata.
/// </summary>
public sealed class FlightPlan
{
    public string OriginIcao { get; init; } = string.Empty;
    public string DestinationIcao { get; init; } = string.Empty;
    public string AlternateIcao { get; init; } = string.Empty;

    public string Callsign { get; init; } = string.Empty;
    public string FlightNumber { get; init; } = string.Empty;

    public string Route { get; init; } = string.Empty;
    public int CruiseFlightLevel { get; init; }
    
    /// <summary>
    /// Ordered list of waypoints from the navlog (enroute waypoints only, excluding SID/STAR).
    /// Used by AeroAI to match SID exit fixes and STAR entry fixes.
    /// </summary>
    public IReadOnlyList<string> WaypointIdentifiers { get; init; } = Array.Empty<string>();

    public override string ToString()
    {
        return $"{Callsign} {OriginIcao}->{DestinationIcao} FL{CruiseFlightLevel} ROUTE {Route}";
    }
}
