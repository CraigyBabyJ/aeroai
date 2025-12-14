namespace AtcNavDataDemo.Models;

public class Procedure
{
    public string AirportIdentifier { get; }
    public string ProcedureIdentifier { get; }
    public string RouteType { get; }
    public string TransitionIdentifier { get; }

    public List<ProcedureLeg> Legs { get; }

    public Procedure(
        string airportIdentifier,
        string procedureIdentifier,
        string routeType,
        string transitionIdentifier,
        List<ProcedureLeg> legs)
    {
        AirportIdentifier = airportIdentifier;
        ProcedureIdentifier = procedureIdentifier;
        RouteType = routeType;
        TransitionIdentifier = transitionIdentifier;
        Legs = legs;
    }
}
