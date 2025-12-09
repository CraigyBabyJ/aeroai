using AeroAI.Atc.Vectoring;

namespace AeroAI.Atc;

internal sealed class StubWaypointResolver : IWaypointResolver
{
	public WaypointPosition? GetWaypointPosition(string waypointIdentifier)
	{
		return null;
	}
}
