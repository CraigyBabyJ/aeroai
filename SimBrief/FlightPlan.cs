namespace AtcNavDataDemo.SimBrief;

/// <summary>
/// Flight plan data from SimBrief.
/// NOTE: AeroAI will prefer SimBrief's planned runway/SID when they pass weather/aircraft checks.
/// Enroute waypoints are always taken from SimBrief; runway/procedure may be overridden by weather.
/// </summary>
public sealed class FlightPlan
{
    public string OriginIcao { get; init; } = string.Empty;
    public string OriginName { get; init; } = string.Empty;
    public string? OriginMetar { get; init; }
    public string DestinationIcao { get; init; } = string.Empty;
    public string DestinationName { get; init; } = string.Empty;
    public string? DestinationMetar { get; init; }
    public string AlternateIcao { get; init; } = string.Empty;
    public string AircraftIcao { get; init; } = string.Empty;

    /// <summary>
    /// Planned departure runway from SimBrief (may be empty if not provided).
    /// </summary>
    public string PlannedDepartureRunway { get; init; } = string.Empty;

    /// <summary>
    /// Planned arrival runway from SimBrief (may be empty if not provided).
    /// </summary>
    public string PlannedArrivalRunway { get; init; } = string.Empty;

    /// <summary>
    /// Planned SID identifier from SimBrief (may be empty if not provided).
    /// </summary>
    public string PlannedSid { get; init; } = string.Empty;

    public string AirlineIcao { get; init; } = string.Empty;

    public string Callsign { get; init; } = string.Empty;
    public string FlightNumber { get; init; } = string.Empty;

    public string Route { get; init; } = string.Empty;
    public int CruiseFlightLevel { get; init; }
    
    /// <summary>
    /// Initial altitude in feet from SimBrief (may be 0 if not provided).
    /// </summary>
    public int InitialAltitude { get; init; }
    
    /// <summary>
    /// Ordered list of waypoints from the navlog (enroute waypoints only, excluding SID/STAR).
    /// Used by AeroAI to match SID exit fixes and STAR entry fixes.
    /// </summary>
    public IReadOnlyList<string> WaypointIdentifiers { get; init; } = Array.Empty<string>();

    public override string ToString()
    {
        return $"{Callsign} {OriginIcao}->{DestinationIcao} FL{CruiseFlightLevel} RWY {PlannedDepartureRunway}->{PlannedArrivalRunway} SID {PlannedSid} ROUTE {Route}";
    }
}
