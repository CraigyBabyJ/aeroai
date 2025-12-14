using System;
using System.Linq;
using AeroAI.Models;

namespace AeroAI.Atc.Vectoring;

public sealed class DepartureVectorGenerator
{
	private enum TurnDirection
	{
		Left,
		Right
	}

	private const int MinRunwayHeadingAltitude = 1500;

	private const int InitialClimbAltitude = 3000;

	private const int TurnToFixAltitude = 5000;

	private const double DirectFixDistanceNm = 5.0;

	public VectorInstruction? GenerateNextInstruction(FlightContext context, SimState simState, IWaypointResolver waypointResolver)
	{
		if (context.DepartureVectors == null)
		{
			return null;
		}
		if (context.DepartureRunway == null)
		{
			return null;
		}
		EnrouteRoute? enrouteRoute = context.EnrouteRoute;
		if (enrouteRoute != null && enrouteRoute.WaypointIdentifiers.Count == 0)
		{
			if (simState.AltitudeFeet < 3000)
			{
				return new VectorInstruction
				{
					Heading = context.DepartureRunway.TrueHeadingDegrees,
					Altitude = 3000,
					Phrase = $"{FormatCallsign(context.Callsign)}, climb runway heading, maintain {3000} feet."
				};
			}
			return null;
		}
		string? text = context.EnrouteRoute?.WaypointIdentifiers[0];
		if (string.IsNullOrWhiteSpace(text))
		{
			return null;
		}
		WaypointPosition? waypointPosition = waypointResolver.GetWaypointPosition(text);
		if (waypointPosition == null)
		{
			if (simState.AltitudeFeet < 3000)
			{
				return new VectorInstruction
				{
					Heading = context.DepartureRunway.TrueHeadingDegrees,
					Altitude = 3000,
					Phrase = $"{FormatCallsign(context.Callsign)}, climb runway heading, maintain {3000} feet."
				};
			}
			return null;
		}
		if (!context.DepartureVectors.HasLeftRunwayHeading)
		{
			if (simState.AltitudeFeet < 1500)
			{
				return new VectorInstruction
				{
					Heading = context.DepartureRunway.TrueHeadingDegrees,
					Altitude = 3000,
					Phrase = $"{FormatCallsign(context.Callsign)}, climb runway heading, maintain {3000} feet."
				};
			}
			context.DepartureVectors.HasLeftRunwayHeading = true;
			int num = (int)CalculateBearing(simState.Latitude, simState.Longitude, waypointPosition.Latitude, waypointPosition.Longitude);
			context.DepartureVectors.TargetHeading = num;
			string value = ((GetTurnDirection(simState.HeadingDegrees, num) == TurnDirection.Left) ? "left" : "right");
			return new VectorInstruction
			{
				Heading = num,
				Altitude = 5000,
				Phrase = $"{FormatCallsign(context.Callsign)}, turn {value} heading {num:D3}, maintain {5000} feet."
			};
		}
		if (!context.DepartureVectors.HasResumedOwnNavigation)
		{
			double num2 = CalculateDistanceNm(simState.Latitude, simState.Longitude, waypointPosition.Latitude, waypointPosition.Longitude);
			if (num2 <= 5.0)
			{
				context.DepartureVectors.HasResumedOwnNavigation = true;
				return new VectorInstruction
				{
					Heading = null,
					Altitude = null,
					Phrase = FormatCallsign(context.Callsign) + ", proceed direct " + text + ", resume own navigation."
				};
			}
			int num3 = (int)CalculateBearing(simState.Latitude, simState.Longitude, waypointPosition.Latitude, waypointPosition.Longitude);
			int num4 = Math.Abs(NormalizeHeading(num3) - NormalizeHeading(simState.HeadingDegrees));
			if (num4 > 10)
			{
				string value2 = ((GetTurnDirection(simState.HeadingDegrees, num3) == TurnDirection.Left) ? "left" : "right");
				context.DepartureVectors.TargetHeading = num3;
				return new VectorInstruction
				{
					Heading = num3,
					Altitude = 5000,
					Phrase = $"{FormatCallsign(context.Callsign)}, turn {value2} heading {num3:D3}, maintain {5000} feet."
				};
			}
		}
		return null;
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
