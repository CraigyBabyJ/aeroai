using System.Collections.Generic;
using AeroAI.Models;

namespace AeroAI.Logic;

public interface IProcedureSelector
{
	SidSelectionResult SelectSidForRoute(string airportIcao, NavRunwaySummary departureRunway, EnrouteRoute route, IReadOnlyList<SidSummary> availableSids);

	StarSelectionResult SelectStarForRoute(string airportIcao, NavRunwaySummary arrivalRunway, EnrouteRoute route, IReadOnlyList<StarSummary> availableStars);

	ApproachSelectionResult SelectApproachForRunway(string airportIcao, NavRunwaySummary arrivalRunway, WeatherInfo weather, IReadOnlyList<ApproachSummary> availableApproaches, StarSelectionResult? starSelection);
}
