namespace AeroAI.Atc.Vectoring;

public interface IWaypointResolver
{
	WaypointPosition? GetWaypointPosition(string waypointIdentifier);
}
