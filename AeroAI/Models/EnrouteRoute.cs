using System;
using System.Collections.Generic;

namespace AeroAI.Models;

public sealed class EnrouteRoute
{
	public IReadOnlyList<string> WaypointIdentifiers { get; init; } = Array.Empty<string>();

	public string OriginIcao { get; init; } = string.Empty;

	public string DestinationIcao { get; init; } = string.Empty;
}
