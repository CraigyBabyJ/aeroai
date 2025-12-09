using System;
using System.Linq;
using System.Text;
using AeroAI.Models;

namespace AeroAI.Atc;

public class AtcResponseGenerator
{
	private readonly Random _random = new Random();

	public string GenerateResponse(PilotIntent intent, FlightContext context)
	{
		StringBuilder stringBuilder = new StringBuilder();
		string text = FormatCallsign(context.Callsign);
		switch (intent.Type)
		{
		case IntentType.RequestClearance:
			return GenerateClearanceDelivery(context);
		case IntentType.ReadbackClearance:
			return text + ", readback correct.";
		case IntentType.RequestPush:
		{
			string pushDirection = GetPushDirection(context.DepartureRunway);
			return text + ", push and start approved, facing " + pushDirection + ".";
		}
		case IntentType.ReadyToTaxi:
			if (context.DepartureRunway == null)
			{
				return text + ", standby.";
			}
			return $"{text}, taxi to holding point runway {context.DepartureRunway.RunwayIdentifier} via [TAXI ROUTE], hold short runway {context.DepartureRunway.RunwayIdentifier}.";
		case IntentType.ReadyForDeparture:
		{
			if (context.DepartureRunway == null)
			{
				return text + ", standby.";
			}
			string value4 = $"{context.OriginWeather.WindDirectionDegrees:D3} at {context.OriginWeather.WindSpeedKnots}";
			return $"{text}, {GetTowerName(context.OriginIcao)} Tower, wind {value4}, runway {context.DepartureRunway.RunwayIdentifier}, line up and wait.";
		}
		case IntentType.AcknowledgeTakeoff:
		{
			if (context.DepartureRunway == null)
			{
				return text + ", standby.";
			}
			string initialAltitude = GetInitialAltitude(context.CruiseFlightLevel);
			SidSelectionResult? selectedSid = context.SelectedSid;
			if (selectedSid != null && selectedSid.Mode == ProcedureSelectionMode.Vectors)
			{
				return $"{text}, runway {context.DepartureRunway.RunwayIdentifier}, cleared for takeoff. Climb runway heading, maintain {initialAltitude} feet.";
			}
			SidSelectionResult? selectedSid2 = context.SelectedSid;
			string value3 = ((selectedSid2 != null && selectedSid2.Mode == ProcedureSelectionMode.Published && selectedSid2.SelectedSid != null) ? selectedSid2.SelectedSid.ProcedureIdentifier : "vectors");
			return $"{text}, runway {context.DepartureRunway.RunwayIdentifier}, cleared for takeoff. Climb {initialAltitude}, {value3} departure.";
		}
		case IntentType.ContactDeparture:
		{
			string departureFrequency = GetDepartureFrequency(context.OriginIcao);
			return $"{text}, contact {GetDepartureName(context.OriginIcao)} on {departureFrequency}, good flight.";
		}
		case IntentType.ClimbAcknowledged:
			if (context.CurrentAltitude < context.CruiseFlightLevel - 10)
			{
				int nextAltitude = GetNextAltitude(context.CurrentAltitude, context.CruiseFlightLevel);
				return $"{text}, climb flight level {nextAltitude}.";
			}
			return $"{text}, maintain flight level {context.CruiseFlightLevel}.";
		case IntentType.ContactArrival:
		{
			string arrivalFrequency = GetArrivalFrequency(context.DestinationIcao);
			string text2 = $"{text}, contact {GetArrivalName(context.DestinationIcao)} on {arrivalFrequency}.";
			StarSelectionResult? selectedStar = context.SelectedStar;
			if (selectedStar != null && selectedStar.Mode == ProcedureSelectionMode.Vectors && context.ArrivalRunway != null)
			{
				string value2 = "ILS";
			ApproachSelectionResult? selectedApproach = context.SelectedApproach;
			if (selectedApproach != null && selectedApproach.Mode == ProcedureSelectionMode.Published && selectedApproach.SelectedApproach != null)
			{
				ApproachSummary selectedApproach2 = selectedApproach.SelectedApproach;
					value2 = ((selectedApproach2.ApproachTypeCode == "I" || selectedApproach2.HasGlideslope) ? "ILS" : "RNAV");
				}
				text2 += $" Expect vectors for the {value2} runway {context.ArrivalRunway.RunwayIdentifier}.";
			}
			return text2;
		}
		case IntentType.RunwayInSight:
		{
			string towerFrequency = GetTowerFrequency(context.DestinationIcao);
			return $"{text}, roger. Contact {GetTowerName(context.DestinationIcao)} Tower on {towerFrequency}.";
		}
		case IntentType.AcknowledgeLanding:
		{
			if (context.ArrivalRunway == null)
			{
				return text + ", standby.";
			}
			string value = $"{context.DestinationWeather.WindDirectionDegrees:D3} at {context.DestinationWeather.WindSpeedKnots}";
			return $"{text}, {GetTowerName(context.DestinationIcao)} Tower, wind {value}, runway {context.ArrivalRunway.RunwayIdentifier}, cleared to land.";
		}
		case IntentType.RequestShutdown:
			return text + ", shutdown approved, good day.";
		case IntentType.CheckIn:
			return $"{text}, {GetAtcUnitName(context.CurrentAtcUnit)} {GetAirportName(context.OriginIcao)}, go ahead.";
		default:
			return text + ", say again?";
		}
	}

	private string GenerateClearanceDelivery(FlightContext context)
	{
		if (context.DepartureRunway == null)
		{
			return FormatCallsign(context.Callsign) + ", standby for clearance.";
		}
		StringBuilder stringBuilder = new StringBuilder();
		string value = FormatCallsign(context.Callsign);
		StringBuilder stringBuilder2 = stringBuilder;
		StringBuilder stringBuilder3 = stringBuilder2;
		StringBuilder.AppendInterpolatedStringHandler handler = new StringBuilder.AppendInterpolatedStringHandler(33, 3, stringBuilder2);
		handler.AppendFormatted(value);
		handler.AppendLiteral(", ");
		handler.AppendFormatted(GetClearanceDeliveryName(context.OriginIcao));
		handler.AppendLiteral(" Clearance, cleared to ");
		handler.AppendFormatted(context.DestinationIcao);
		handler.AppendLiteral(" airport");
		stringBuilder3.Append(ref handler);
		SidSelectionResult? selectedSid = context.SelectedSid;
		if (selectedSid != null && selectedSid.Mode == ProcedureSelectionMode.Published && selectedSid.SelectedSid != null)
		{
			stringBuilder2 = stringBuilder;
			StringBuilder stringBuilder4 = stringBuilder2;
			handler = new StringBuilder.AppendInterpolatedStringHandler(15, 1, stringBuilder2);
			handler.AppendLiteral(" via ");
			handler.AppendFormatted(selectedSid.SelectedSid.ProcedureIdentifier);
			handler.AppendLiteral(" departure");
			stringBuilder4.Append(ref handler);
			if (!string.IsNullOrWhiteSpace(selectedSid.MatchingExitFix))
			{
				stringBuilder2 = stringBuilder;
				StringBuilder stringBuilder5 = stringBuilder2;
				handler = new StringBuilder.AppendInterpolatedStringHandler(13, 1, stringBuilder2);
				handler.AppendLiteral(", ");
				handler.AppendFormatted(selectedSid.MatchingExitFix);
				handler.AppendLiteral(" transition");
				stringBuilder5.Append(ref handler);
			}
		}
		else
		{
			stringBuilder.Append(" via radar vectors");
		}
		stringBuilder.Append(", then as filed.");
		string initialAltitude = GetInitialAltitude(context.CruiseFlightLevel);
		if (context.SquawkCode == null)
		{
			context.SquawkCode = GenerateSquawk();
		}
		stringBuilder.AppendLine();
		stringBuilder2 = stringBuilder;
		StringBuilder stringBuilder6 = stringBuilder2;
		handler = new StringBuilder.AppendInterpolatedStringHandler(93, 4, stringBuilder2);
		handler.AppendLiteral("Departure runway ");
		handler.AppendFormatted(context.DepartureRunway.RunwayIdentifier);
		handler.AppendLiteral(", initial climb ");
		handler.AppendFormatted(initialAltitude);
		handler.AppendLiteral(", squawk ");
		handler.AppendFormatted(context.SquawkCode);
		handler.AppendLiteral(", expect flight level ");
		handler.AppendFormatted(context.CruiseFlightLevel);
		handler.AppendLiteral(" ten minutes after departure.");
		stringBuilder6.Append(ref handler);
		return stringBuilder.ToString();
	}

	private string FormatCallsign(string callsign)
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

	private string GenerateSquawk()
	{
		return $"{_random.Next(1, 8)}{_random.Next(0, 8)}{_random.Next(0, 8)}{_random.Next(0, 8)}";
	}

	private string GetInitialAltitude(int cruiseFl)
	{
		return (cruiseFl > 300) ? "five thousand feet" : "three thousand feet";
	}

	private int GetNextAltitude(int current, int cruise)
	{
		return Math.Min(current + 60, cruise);
	}

	private string GetPushDirection(NavRunwaySummary? runway)
	{
		if (runway == null)
		{
			return "west";
		}
		int trueHeadingDegrees = runway.TrueHeadingDegrees;
		if (trueHeadingDegrees >= 315 || trueHeadingDegrees < 45)
		{
			return "north";
		}
		if (trueHeadingDegrees >= 45 && trueHeadingDegrees < 135)
		{
			return "east";
		}
		if (trueHeadingDegrees >= 135 && trueHeadingDegrees < 225)
		{
			return "south";
		}
		return "west";
	}

	private string GetClearanceDeliveryName(string icao)
	{
		return GetAirportName(icao);
	}

	private string GetGroundName(string icao)
	{
		return GetAirportName(icao);
	}

	private string GetTowerName(string icao)
	{
		return GetAirportName(icao);
	}

	private string GetDepartureName(string icao)
	{
		return GetAirportName(icao);
	}

	private string GetArrivalName(string icao)
	{
		return GetAirportName(icao);
	}

	private string GetAirportName(string icao)
	{
		if (1 == 0)
		{
		}
		string result = icao switch
		{
			"EDDM" => "Munich", 
			"LOWI" => "Innsbruck", 
			"KJFK" => "New York", 
			"KLAX" => "Los Angeles", 
			_ => icao, 
		};
		if (1 == 0)
		{
		}
		return result;
	}

	private string GetAtcUnitName(AtcUnit unit)
	{
		if (1 == 0)
		{
		}
		string result = unit switch
		{
			AtcUnit.ClearanceDelivery => "Clearance", 
			AtcUnit.Ground => "Ground", 
			AtcUnit.Tower => "Tower", 
			AtcUnit.Departure => "Departure", 
			AtcUnit.Center => "Center", 
			AtcUnit.Arrival => "Arrival", 
			AtcUnit.Approach => "Approach", 
			_ => "ATC", 
		};
		if (1 == 0)
		{
		}
		return result;
	}

	private string GetDepartureFrequency(string icao)
	{
		return "119.2";
	}

	private string GetArrivalFrequency(string icao)
	{
		return "120.1";
	}

	private string GetTowerFrequency(string icao)
	{
		return "118.1";
	}

	private string GetGroundFrequency(string icao)
	{
		return "121.7";
	}
}
