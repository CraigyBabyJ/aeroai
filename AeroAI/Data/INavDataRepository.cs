using System.Collections.Generic;
using AeroAI.Models;

namespace AeroAI.Data;

public interface INavDataRepository
{
	IReadOnlyList<NavRunwaySummary> GetRunways(string airportIcao);

	IReadOnlyList<SidSummary> GetSids(string airportIcao);

	IReadOnlyList<StarSummary> GetStars(string airportIcao);

	IReadOnlyList<ApproachSummary> GetApproaches(string airportIcao);
}
