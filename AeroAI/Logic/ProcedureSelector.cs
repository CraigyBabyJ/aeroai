using System;
using System.Collections.Generic;
using System.Linq;
using AeroAI.Models;

namespace AeroAI.Logic;

public sealed class ProcedureSelector : IProcedureSelector
{
	public SidSelectionResult SelectSidForRoute(string airportIcao, NavRunwaySummary departureRunway, EnrouteRoute route, IReadOnlyList<SidSummary> availableSids)
	{
		List<SidSummary> list = (from s in availableSids
			where string.Equals(s.AirportIcao, airportIcao, StringComparison.OrdinalIgnoreCase)
			where string.IsNullOrWhiteSpace(s.RunwayIdentifier) || string.Equals(s.RunwayIdentifier, departureRunway.RunwayIdentifier, StringComparison.OrdinalIgnoreCase) || string.Equals(s.RunwayIdentifier, "ALL", StringComparison.OrdinalIgnoreCase)
			select s).ToList();
		if (list.Count == 0)
		{
			return new SidSelectionResult
			{
				Mode = ProcedureSelectionMode.Vectors,
				SelectedSid = null,
				MatchingExitFix = null,
				Reason = "No SIDs available for departure runway; assigning vectors departure."
			};
		}
		if (route.WaypointIdentifiers.Count == 0)
		{
			return new SidSelectionResult
			{
				Mode = ProcedureSelectionMode.Vectors,
				SelectedSid = null,
				MatchingExitFix = null,
				Reason = "No enroute waypoints available; assigning vectors departure."
			};
		}
		double num = double.NegativeInfinity;
		SidSummary? sidSummary = null;
		string? text = null;
		for (int num2 = 0; num2 < list.Count; num2++)
		{
			SidSummary sidSummary2 = list[num2];
			string exitFixIdentifier = sidSummary2.ExitFixIdentifier;
			if (string.IsNullOrWhiteSpace(exitFixIdentifier))
			{
				continue;
			}
			int num3 = IndexOfIgnoreCase(route.WaypointIdentifiers, exitFixIdentifier);
			if (num3 < 0)
			{
				continue;
			}
			int num4 = Math.Min(5, route.WaypointIdentifiers.Count - 1);
			if (num3 <= num4)
			{
				double num5 = 100.0 - (double)num3 * 10.0;
				if (num5 > num)
				{
					num = num5;
					sidSummary = sidSummary2;
					text = route.WaypointIdentifiers[num3];
				}
			}
		}
		if (sidSummary == null)
		{
			return new SidSelectionResult
			{
				Mode = ProcedureSelectionMode.Vectors,
				SelectedSid = null,
				MatchingExitFix = null,
				Reason = "No SID exit fix matched early enroute waypoints; assigning vectors departure."
			};
		}
		return new SidSelectionResult
		{
			Mode = ProcedureSelectionMode.Published,
			SelectedSid = sidSummary,
			MatchingExitFix = text,
			Reason = $"Selected SID {sidSummary.ProcedureIdentifier} matching enroute route via exit fix {text}."
		};
	}

	public StarSelectionResult SelectStarForRoute(string airportIcao, NavRunwaySummary arrivalRunway, EnrouteRoute route, IReadOnlyList<StarSummary> availableStars)
	{
		List<StarSummary> list = (from s in availableStars
			where string.Equals(s.AirportIcao, airportIcao, StringComparison.OrdinalIgnoreCase)
			where string.IsNullOrWhiteSpace(s.RunwayIdentifier) || string.Equals(s.RunwayIdentifier, arrivalRunway.RunwayIdentifier, StringComparison.OrdinalIgnoreCase) || string.Equals(s.RunwayIdentifier, "ALL", StringComparison.OrdinalIgnoreCase)
			select s).ToList();
		if (list.Count == 0)
		{
			return new StarSelectionResult
			{
				Mode = ProcedureSelectionMode.Vectors,
				SelectedStar = null,
				MatchingEntryFix = null,
				Reason = "No STARs available for arrival runway; assigning vectors arrival."
			};
		}
		if (route.WaypointIdentifiers.Count == 0)
		{
			return new StarSelectionResult
			{
				Mode = ProcedureSelectionMode.Vectors,
				SelectedStar = null,
				MatchingEntryFix = null,
				Reason = "No enroute waypoints available; assigning vectors arrival."
			};
		}
		double num = double.NegativeInfinity;
		StarSummary? starSummary = null;
		string? text = null;
		for (int num2 = 0; num2 < list.Count; num2++)
		{
			StarSummary starSummary2 = list[num2];
			string entryFixIdentifier = starSummary2.EntryFixIdentifier;
			if (string.IsNullOrWhiteSpace(entryFixIdentifier))
			{
				continue;
			}
			int num3 = LastIndexOfIgnoreCase(route.WaypointIdentifiers, entryFixIdentifier);
			if (num3 < 0)
			{
				continue;
			}
			int num4 = route.WaypointIdentifiers.Count - 1;
			int num5 = num4 - num3;
			if (num5 <= 5)
			{
				double num6 = 100.0 - (double)num5 * 10.0;
				if (num6 > num)
				{
					num = num6;
					starSummary = starSummary2;
					text = route.WaypointIdentifiers[num3];
				}
			}
		}
		if (starSummary == null)
		{
			return new StarSelectionResult
			{
				Mode = ProcedureSelectionMode.Vectors,
				SelectedStar = null,
				MatchingEntryFix = null,
				Reason = "No STAR entry fix matched final enroute waypoints; assigning vectors arrival."
			};
		}
		return new StarSelectionResult
		{
			Mode = ProcedureSelectionMode.Published,
			SelectedStar = starSummary,
			MatchingEntryFix = text,
			Reason = $"Selected STAR {starSummary.ProcedureIdentifier} matching enroute route via entry fix {text}."
		};
	}

	public ApproachSelectionResult SelectApproachForRunway(string airportIcao, NavRunwaySummary arrivalRunway, WeatherInfo weather, IReadOnlyList<ApproachSummary> availableApproaches, StarSelectionResult? starSelection)
	{
		List<ApproachSummary> list = (from a in availableApproaches
			where string.Equals(a.AirportIcao, airportIcao, StringComparison.OrdinalIgnoreCase)
			where string.Equals(a.RunwayIdentifier, arrivalRunway.RunwayIdentifier, StringComparison.OrdinalIgnoreCase)
			select a).ToList();
		if (list.Count == 0)
		{
			return new ApproachSelectionResult
			{
				Mode = ProcedureSelectionMode.Vectors,
				SelectedApproach = null,
				Reason = "No published approaches for runway; vectors/visual only."
			};
		}
		bool flag = IsImc(weather);
		double num = double.NegativeInfinity;
		ApproachSummary? approachSummary = null;
		foreach (ApproachSummary item in list)
		{
			double num2 = 0.0;
			if (flag)
			{
				num2 = ((!item.HasGlideslope && !string.Equals(item.ApproachTypeCode, "I", StringComparison.OrdinalIgnoreCase)) ? ((!item.IsRnav) ? (num2 - 50.0) : (num2 + 60.0)) : (num2 + 100.0));
			}
			else
			{
				if (item.HasGlideslope || string.Equals(item.ApproachTypeCode, "I", StringComparison.OrdinalIgnoreCase))
				{
					num2 += 60.0;
				}
				if (item.IsRnav)
				{
					num2 += 50.0;
				}
			}
			if (flag && !item.SupportsStraightIn)
			{
				num2 -= 20.0;
			}
			if (starSelection != null && starSelection.Mode == ProcedureSelectionMode.Published && starSelection.SelectedStar != null)
			{
				string exitFixIdentifier = starSelection.SelectedStar.ExitFixIdentifier;
				if (!string.IsNullOrWhiteSpace(exitFixIdentifier) && string.Equals(item.InitialApproachFixIdentifier, exitFixIdentifier, StringComparison.OrdinalIgnoreCase))
				{
					num2 += 40.0;
				}
			}
			if (num2 > num)
			{
				num = num2;
				approachSummary = item;
			}
		}
		if (approachSummary == null)
		{
			return new ApproachSelectionResult
			{
				Mode = ProcedureSelectionMode.Vectors,
				SelectedApproach = null,
				Reason = "Could not score any approach; vectors to runway."
			};
		}
		return new ApproachSelectionResult
		{
			Mode = ProcedureSelectionMode.Published,
			SelectedApproach = approachSummary,
			Reason = "Selected approach " + approachSummary.ProcedureIdentifier + " based on IMC/VMC and STAR connectivity."
		};
	}

	private static bool IsImc(WeatherInfo weather)
	{
		if (weather.CeilingFeet > 0 && weather.CeilingFeet < 1000)
		{
			return true;
		}
		if (weather.VisibilityMeters > 0 && weather.VisibilityMeters < 4800)
		{
			return true;
		}
		return weather.IsIfr;
	}

	private static int IndexOfIgnoreCase(IReadOnlyList<string> list, string value)
	{
		for (int i = 0; i < list.Count; i++)
		{
			if (string.Equals(list[i], value, StringComparison.OrdinalIgnoreCase))
			{
				return i;
			}
		}
		return -1;
	}

	private static int LastIndexOfIgnoreCase(IReadOnlyList<string> list, string value)
	{
		for (int num = list.Count - 1; num >= 0; num--)
		{
			if (string.Equals(list[num], value, StringComparison.OrdinalIgnoreCase))
			{
				return num;
			}
		}
		return -1;
	}
}
