namespace AtcNavDataDemo.Models;

public enum PathTermination
{
    Unknown = 0,
    IF,
    TF,
    CF,
    RF,
    VA,
    VM,
    VI,
    VD
}

public enum AltitudeConstraintType
{
    None = 0,
    At,
    AtOrAbove,
    AtOrBelow,
    Between
}

/// <summary>
/// Represents one row of tbl_pathpoints.
/// </summary>
public class ProcedureLeg
{
    public string AirportIdentifier { get; }
    public string ProcedureIdentifier { get; }
    public string RouteType { get; }
    public string TransitionIdentifier { get; }
    public int Seqno { get; }

    public string WaypointIdentifier { get; }
    public double WaypointLatitude { get; }
    public double WaypointLongitude { get; }

    public string PathTerminationRaw { get; }
    public PathTermination PathTermination { get; }

    public string AltitudeDescriptionRaw { get; }
    public AltitudeConstraintType AltitudeConstraintType { get; }
    public int Altitude1 { get; }
    public int Altitude2 { get; }

    public string SpeedLimitDescription { get; }
    public int SpeedLimit { get; }

    public ProcedureLeg(
        string airportIdentifier,
        string procedureIdentifier,
        string routeType,
        string transitionIdentifier,
        int seqno,
        string waypointIdentifier,
        double waypointLatitude,
        double waypointLongitude,
        string pathTerminationRaw,
        string altitudeDescriptionRaw,
        int altitude1,
        int altitude2,
        string speedLimitDescription,
        int speedLimit)
    {
        AirportIdentifier = airportIdentifier;
        ProcedureIdentifier = procedureIdentifier;
        RouteType = routeType;
        TransitionIdentifier = transitionIdentifier;
        Seqno = seqno;

        WaypointIdentifier = waypointIdentifier;
        WaypointLatitude = waypointLatitude;
        WaypointLongitude = waypointLongitude;

        PathTerminationRaw = pathTerminationRaw;
        PathTermination = ParsePathTermination(pathTerminationRaw);

        AltitudeDescriptionRaw = altitudeDescriptionRaw;
        AltitudeConstraintType = ParseAltitudeDescription(altitudeDescriptionRaw);
        Altitude1 = altitude1;
        Altitude2 = altitude2;

        SpeedLimitDescription = speedLimitDescription;
        SpeedLimit = speedLimit;
    }

    private static PathTermination ParsePathTermination(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return PathTermination.Unknown;

        if (Enum.TryParse<PathTermination>(value.Trim(), true, out var parsed))
            return parsed;

        return PathTermination.Unknown;
    }

    private static AltitudeConstraintType ParseAltitudeDescription(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return AltitudeConstraintType.None;

        var v = value.Trim().ToUpperInvariant();

        // These mappings are heuristic and may be refined based on actual PMDG codes.
        return v switch
        {
            "A" or "ABOVE" or "AT OR ABOVE" or "+" => AltitudeConstraintType.AtOrAbove,
            "B" or "BELOW" or "AT OR BELOW" or "-" => AltitudeConstraintType.AtOrBelow,
            "BETWEEN" or "AB" => AltitudeConstraintType.Between,
            "AT" or "H" => AltitudeConstraintType.At,
            _ => AltitudeConstraintType.None
        };
    }
}
