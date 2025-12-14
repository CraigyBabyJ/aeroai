using System.Collections.Generic;
using AeroAI.Models;

namespace AeroAI.Logic;

public interface IRunwaySelector
{
	RunwaySelectionResult SelectDepartureRunway(string airportIcao, WeatherInfo weather, AircraftPerformanceProfile aircraft, IReadOnlyList<NavRunwaySummary> availableRunways);

	RunwaySelectionResult SelectArrivalRunway(string airportIcao, WeatherInfo weather, AircraftPerformanceProfile aircraft, IReadOnlyList<NavRunwaySummary> availableRunways);
}
