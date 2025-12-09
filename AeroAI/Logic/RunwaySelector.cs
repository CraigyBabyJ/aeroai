using System;
using System.Collections.Generic;
using System.Linq;
using AeroAI.Models;

namespace AeroAI.Logic;

public sealed class RunwaySelector : IRunwaySelector
{
	private sealed class ScoredRunway
	{
		public required NavRunwaySummary Runway { get; init; }

		public required double Score { get; init; }

		public required double Headwind { get; init; }

		public required double Tailwind { get; init; }

		public required double Crosswind { get; init; }
	}

	private const double RequiredLengthMarginFactor = 1.3;

	public RunwaySelectionResult SelectDepartureRunway(string airportIcao, WeatherInfo weather, AircraftPerformanceProfile aircraft, IReadOnlyList<NavRunwaySummary> availableRunways)
	{
		IReadOnlyList<NavRunwaySummary> scoredRunways;
		List<ScoredRunway> list = FilterAndScoreRunways(availableRunways, weather, aircraft, isDeparture: true, out scoredRunways);
		if (list.Count == 0)
		{
			NavRunwaySummary? selectedRunway = ChooseFallbackRunway(availableRunways, weather, aircraft);
			return new RunwaySelectionResult
			{
				SelectedRunway = selectedRunway,
				CandidatesConsidered = availableRunways.ToArray(),
				Reason = "No runway met strict departure criteria; chose fallback by length/tailwind."
			};
		}
		ScoredRunway scoredRunway = list.OrderByDescending((ScoredRunway c) => c.Score).First();
		return new RunwaySelectionResult
		{
			SelectedRunway = scoredRunway.Runway,
			CandidatesConsidered = scoredRunways,
			Reason = "Selected departure runway " + scoredRunway.Runway.RunwayIdentifier + " based on headwind/length and config preferences."
		};
	}

	public RunwaySelectionResult SelectArrivalRunway(string airportIcao, WeatherInfo weather, AircraftPerformanceProfile aircraft, IReadOnlyList<NavRunwaySummary> availableRunways)
	{
		IReadOnlyList<NavRunwaySummary> scoredRunways;
		List<ScoredRunway> list = FilterAndScoreRunways(availableRunways, weather, aircraft, isDeparture: false, out scoredRunways);
		if (list.Count == 0)
		{
			NavRunwaySummary? selectedRunway = ChooseFallbackRunway(availableRunways, weather, aircraft);
			return new RunwaySelectionResult
			{
				SelectedRunway = selectedRunway,
				CandidatesConsidered = availableRunways.ToArray(),
				Reason = "No runway met strict arrival criteria; chose fallback by length/tailwind and approach availability."
			};
		}
		ScoredRunway scoredRunway = list.OrderByDescending((ScoredRunway c) => c.Score).First();
		return new RunwaySelectionResult
		{
			SelectedRunway = scoredRunway.Runway,
			CandidatesConsidered = scoredRunways,
			Reason = "Selected arrival runway " + scoredRunway.Runway.RunwayIdentifier + " based on headwind/length and precision approach availability."
		};
	}

	private static List<ScoredRunway> FilterAndScoreRunways(IReadOnlyList<NavRunwaySummary> availableRunways, WeatherInfo weather, AircraftPerformanceProfile aircraft, bool isDeparture, out IReadOnlyList<NavRunwaySummary> scoredRunways)
	{
		List<ScoredRunway> list = new List<ScoredRunway>();
		foreach (NavRunwaySummary availableRunway in availableRunways)
		{
			(double Headwind, double Crosswind) tuple = ComputeWindComponents(availableRunway.TrueHeadingDegrees, weather.WindDirectionDegrees, weather.WindSpeedKnots);
			double item = tuple.Headwind;
			double item2 = tuple.Crosswind;
			double num = Math.Max(0.0, 0.0 - item);
			double num2 = Math.Abs(item2);
			if (num > (double)aircraft.MaxTailwindComponentKnots || num2 > (double)aircraft.MaxCrosswindComponentKnots)
			{
				continue;
			}
			int num3 = (isDeparture ? aircraft.RequiredTakeoffDistanceFeet : aircraft.RequiredLandingDistanceFeet);
			int num4 = (int)((double)num3 * 1.3);
			if (availableRunway.LengthFeet >= num4 && (isDeparture || !weather.IsIfr || availableRunway.HasIlsOrLocalizer || availableRunway.HasRnavApproach))
			{
				double num5 = 0.0;
				num5 += item * 2.0;
				num5 -= num2 * 0.5;
				num5 += Math.Min((double)availableRunway.LengthFeet / 100.0, 50.0);
				if (!isDeparture && weather.IsIfr && availableRunway.HasIlsOrLocalizer)
				{
					num5 += 40.0;
				}
				if (isDeparture && availableRunway.IsPreferredDeparture)
				{
					num5 += 10.0;
				}
				if (!isDeparture && availableRunway.IsPreferredArrival)
				{
					num5 += 10.0;
				}
				list.Add(new ScoredRunway
				{
					Runway = availableRunway,
					Score = num5,
					Headwind = item,
					Tailwind = num,
					Crosswind = num2
				});
			}
		}
		scoredRunways = list.Select((ScoredRunway c) => c.Runway).Distinct().ToArray();
		return list;
	}

	private static NavRunwaySummary? ChooseFallbackRunway(IReadOnlyList<NavRunwaySummary> availableRunways, WeatherInfo weather, AircraftPerformanceProfile aircraft)
	{
		if (availableRunways.Count == 0)
		{
			return null;
		}
		List<(NavRunwaySummary Runway, double Tailwind, double Crosswind)> list = new List<(NavRunwaySummary Runway, double Tailwind, double Crosswind)>();
		foreach (NavRunwaySummary availableRunway in availableRunways)
		{
			(double Headwind, double Crosswind) tuple = ComputeWindComponents(availableRunway.TrueHeadingDegrees, weather.WindDirectionDegrees, weather.WindSpeedKnots);
			double item = tuple.Headwind;
			double item2 = tuple.Crosswind;
			double item3 = Math.Max(0.0, 0.0 - item);
			list.Add((availableRunway, item3, Math.Abs(item2)));
		}
		return (from s in list
			orderby s.Tailwind, s.Runway.LengthFeet descending
			select s.Runway).FirstOrDefault();
	}

	private static (double Headwind, double Crosswind) ComputeWindComponents(int runwayHeadingDeg, int windDirectionDeg, int windSpeedKnots)
	{
		if (windSpeedKnots <= 1)
		{
			return (Headwind: 0.0, Crosswind: 0.0);
		}
		int num = NormalizeAngleDegrees(runwayHeadingDeg);
		int num2 = NormalizeAngleDegrees(windDirectionDeg);
		int num3 = NormalizeAngleDegrees(num2 - num);
		double num4 = (double)num3 * Math.PI / 180.0;
		double item = (double)windSpeedKnots * Math.Cos(num4);
		double item2 = (double)windSpeedKnots * Math.Sin(num4);
		return (Headwind: item, Crosswind: item2);
	}

	private static int NormalizeAngleDegrees(int angle)
	{
		int num = angle % 360;
		if (num < 0)
		{
			num += 360;
		}
		return num;
	}
}
