using System;
using System.Collections.Generic;
using System.Linq;
using AeroAI.Models;

namespace AeroAI.Atc.Vectoring;

public sealed class ArrivalVectorGenerator
{
	private enum TurnDirection
	{
		Left,
		Right
	}

	private const double InterceptDistanceNm = 10.0;

	private const int PositioningAltitude = 7000;

	private const int DescentAltitude = 5000;

	private const int InterceptAltitude = 4000;

	private const double InterceptAngleDegrees = 30.0;

	public VectorInstruction? GenerateNextInstruction(FlightContext context, SimState simState, IWaypointResolver waypointResolver)
	{
		if (context.ArrivalVectors == null)
		{
			return null;
		}
		if (context.ArrivalRunway == null)
		{
			return null;
		}
		if (!context.ArrivalVectors.InterceptLatitude.HasValue)
		{
			InitializeInterceptPoint(context, waypointResolver);
		}
		if (!context.ArrivalVectors.InterceptLatitude.HasValue || !context.ArrivalVectors.InterceptLongitude.HasValue)
		{
			return null;
		}
		double value = context.ArrivalVectors.InterceptLatitude.Value;
		double value2 = context.ArrivalVectors.InterceptLongitude.Value;
		int num = context.ArrivalVectors.FinalApproachCourse ?? context.ArrivalRunway.TrueHeadingDegrees;
		double num2 = CalculateDistanceNm(simState.Latitude, simState.Longitude, value, value2);
		if (context.ArrivalVectors.Phase == ArrivalVectorPhase.Positioning)
		{
			if (num2 > 15.0)
			{
				int num3 = (int)CalculateBearing(simState.Latitude, simState.Longitude, value, value2);
				int num4 = Math.Abs(NormalizeHeading(num3) - NormalizeHeading(simState.HeadingDegrees));
				if (num4 > 15)
				{
					string text = ((GetTurnDirection(simState.HeadingDegrees, num3) == TurnDirection.Left) ? "left" : "right");
					return new VectorInstruction
					{
						Heading = num3,
						Altitude = 7000,
						Phrase = $"{FormatCallsign(context.Callsign)}, fly heading {num3:D3}, descend {7000} feet."
					};
				}
				if (simState.AltitudeFeet > 7000)
				{
					return new VectorInstruction
					{
						Heading = null,
						Altitude = 7000,
						Phrase = $"{FormatCallsign(context.Callsign)}, descend {7000} feet."
					};
				}
			}
			else
			{
				context.ArrivalVectors.Phase = ArrivalVectorPhase.Intercepting;
			}
		}
		if (context.ArrivalVectors.Phase == ArrivalVectorPhase.Intercepting)
		{
			int num5 = NormalizeHeading(num - 90);
			double num6 = CalculateDistanceToFinalCourse(simState.Latitude, simState.Longitude, value, value2, num);
			if (num6 > 2.0)
			{
				string value3 = ((GetTurnDirection(simState.HeadingDegrees, num5) == TurnDirection.Left) ? "left" : "right");
				return new VectorInstruction
				{
					Heading = num5,
					Altitude = 5000,
					Phrase = $"{FormatCallsign(context.Callsign)}, turn {value3} heading {num5:D3}, descend {5000} feet."
				};
			}
			context.ArrivalVectors.Phase = ArrivalVectorPhase.Established;
		}
		if (context.ArrivalVectors.Phase == ArrivalVectorPhase.Established && !context.ArrivalVectors.ClearedForApproach)
		{
			int num7 = Math.Abs(NormalizeHeading(num) - NormalizeHeading(simState.HeadingDegrees));
			if (num7 > 10)
			{
				string value4 = ((GetTurnDirection(simState.HeadingDegrees, num) == TurnDirection.Left) ? "left" : "right");
				return new VectorInstruction
				{
					Heading = num,
					Altitude = 4000,
					Phrase = $"{FormatCallsign(context.Callsign)}, turn {value4} heading {num:D3} to intercept the localizer runway {context.ArrivalRunway.RunwayIdentifier}, maintain {4000} feet until established."
				};
			}
			context.ArrivalVectors.ClearedForApproach = true;
			string approachType = GetApproachType(context);
			return new VectorInstruction
			{
				Heading = num,
				Altitude = 4000,
				Phrase = $"{FormatCallsign(context.Callsign)}, maintain {4000} feet until established, cleared {approachType} runway {context.ArrivalRunway.RunwayIdentifier}."
			};
		}
		return null;
	}

	private void InitializeInterceptPoint(FlightContext context, IWaypointResolver waypointResolver)
	{
		if (context.ArrivalRunway == null)
		{
			return;
		}
		int trueHeadingDegrees = context.ArrivalRunway.TrueHeadingDegrees;
		int num = NormalizeHeading(trueHeadingDegrees + 180);
		EnrouteRoute? enrouteRoute = context.EnrouteRoute;
		if (enrouteRoute == null || enrouteRoute.WaypointIdentifiers.Count <= 0)
		{
			return;
		}
		IReadOnlyList<string> waypointIdentifiers = context.EnrouteRoute!.WaypointIdentifiers;
		string waypointIdentifier = waypointIdentifiers[waypointIdentifiers.Count - 1];
		WaypointPosition? waypointPosition = waypointResolver.GetWaypointPosition(waypointIdentifier);
		if (waypointPosition != null)
		{
			double latitude = waypointPosition.Latitude;
			double longitude = waypointPosition.Longitude;
			if (context.ArrivalVectors != null)
			{
				context.ArrivalVectors.InterceptLatitude = latitude;
				context.ArrivalVectors.InterceptLongitude = longitude;
				context.ArrivalVectors.FinalApproachCourse = trueHeadingDegrees;
			}
		}
	}

	private static double CalculateDistanceToFinalCourse(double lat, double lon, double interceptLat, double interceptLon, int finalCourse)
	{
		return CalculateDistanceNm(lat, lon, interceptLat, interceptLon);
	}

	private static string GetApproachType(FlightContext context)
	{
		ApproachSelectionResult? selectedApproach = context.SelectedApproach;
		if (selectedApproach != null && selectedApproach.Mode == ProcedureSelectionMode.Published && selectedApproach.SelectedApproach != null)
		{
			ApproachSummary selectedApproach2 = selectedApproach.SelectedApproach;
			if (selectedApproach2.ApproachTypeCode == "I" || selectedApproach2.HasGlideslope)
			{
				return "ILS";
			}
			if (selectedApproach2.IsRnav)
			{
				return "RNAV";
			}
			return "visual";
		}
		return "ILS";
	}

	private static double CalculateBearing(double lat1, double lon1, double lat2, double lon2)
	{
		double num = ToRadians(lon2 - lon1);
		double num2 = ToRadians(lat1);
		double num3 = ToRadians(lat2);
		double y = Math.Sin(num) * Math.Cos(num3);
		double x = Math.Cos(num2) * Math.Sin(num3) - Math.Sin(num2) * Math.Cos(num3) * Math.Cos(num);
		double a = ToDegrees(Math.Atan2(y, x));
		return NormalizeHeading((int)Math.Round(a));
	}

	private static double CalculateDistanceNm(double lat1, double lon1, double lat2, double lon2)
	{
		double num = ToRadians(lat2 - lat1);
		double num2 = ToRadians(lon2 - lon1);
		double num3 = Math.Sin(num / 2.0) * Math.Sin(num / 2.0) + Math.Cos(ToRadians(lat1)) * Math.Cos(ToRadians(lat2)) * Math.Sin(num2 / 2.0) * Math.Sin(num2 / 2.0);
		double num4 = 2.0 * Math.Atan2(Math.Sqrt(num3), Math.Sqrt(1.0 - num3));
		return 3440.065 * num4;
	}

	private static TurnDirection GetTurnDirection(int currentHeading, int targetHeading)
	{
		int num = NormalizeHeading(targetHeading) - NormalizeHeading(currentHeading);
		if (num < 0)
		{
			num += 360;
		}
		return (num <= 180) ? TurnDirection.Right : TurnDirection.Left;
	}

	private static int NormalizeHeading(double heading)
	{
		int i;
		for (i = (int)Math.Round(heading); i < 0; i += 360)
		{
		}
		while (i >= 360)
		{
			i -= 360;
		}
		return i;
	}

	private static double ToRadians(double degrees)
	{
		return degrees * Math.PI / 180.0;
	}

	private static double ToDegrees(double radians)
	{
		return radians * 180.0 / Math.PI;
	}

	private static string FormatCallsign(string callsign)
	{
		string[] array = callsign.Split(' ');
		if (array.Length > 1)
		{
			string text = array[0];
			string source = array[1];
			string text2 = string.Join(" ", source.Select(delegate(char c)
			{
				if (1 == 0)
				{
				}
				string result = c switch
				{
					'0' => "zero", 
					'1' => "one", 
					'2' => "two", 
					'3' => "three", 
					'4' => "four", 
					'5' => "five", 
					'6' => "six", 
					'7' => "seven", 
					'8' => "eight", 
					'9' => "nine", 
					_ => c.ToString(), 
				};
				if (1 == 0)
				{
				}
				return result;
			}));
			return text + " " + text2;
		}
		return callsign;
	}
}
