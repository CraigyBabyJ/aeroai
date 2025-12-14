using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using AeroAI.Atc;
using AeroAI.Models;

namespace AeroAI.Llm;

public static class OpenAiPromptTemplate
{
	public static string BuildPrompt(string pilotTransmission, FlightContext context)
	{
		StringBuilder stringBuilder = new StringBuilder();
		object value = BuildContextJson(context);
		string value2 = JsonSerializer.Serialize(value, new JsonSerializerOptions
		{
			WriteIndented = true
		});
		stringBuilder.AppendLine("CONTEXT_JSON:");
		stringBuilder.AppendLine("```json");
		stringBuilder.AppendLine(value2);
		stringBuilder.AppendLine("```");
		stringBuilder.AppendLine();
		stringBuilder.AppendLine("PILOT_TRANSMISSION:");
		StringBuilder stringBuilder2 = stringBuilder;
		StringBuilder.AppendInterpolatedStringHandler handler = new StringBuilder.AppendInterpolatedStringHandler(2, 1, stringBuilder2);
		handler.AppendLiteral("\"");
		handler.AppendFormatted(pilotTransmission);
		handler.AppendLiteral("\"");
		stringBuilder2.AppendLine(ref handler);
		stringBuilder.AppendLine();
		stringBuilder.AppendLine("---");
		stringBuilder.AppendLine();
		stringBuilder.AppendLine("Respond with ONE ATC transmission only. Use the data from CONTEXT_JSON. Do not invent any values.");
		return stringBuilder.ToString();
	}

	private static object BuildContextJson(FlightContext context)
	{
		FlightPhase currentPhase = context.CurrentPhase;
		if (1 == 0)
		{
		}
		string text = currentPhase switch
		{
			FlightPhase.Preflight_Clearance => "CLEARANCE", 
			FlightPhase.Taxi_Out => "TAXI_OUT", 
			FlightPhase.Lineup_Takeoff => (context.DepartureRunway != null) ? "LINEUP" : "FINAL", 
			FlightPhase.Climb_Departure => "CLIMB", 
			FlightPhase.Enroute => "ENROUTE", 
			FlightPhase.Descent_Arrival => "DESCENT", 
			FlightPhase.Approach => "FINAL", 
			FlightPhase.Landing => "FINAL", 
			FlightPhase.Taxi_In => "TAXI_IN", 
			_ => "CLEARANCE", 
		};
		if (1 == 0)
		{
		}
		string phase = text;
		AtcUnit currentAtcUnit = context.CurrentAtcUnit;
		if (1 == 0)
		{
		}
		text = currentAtcUnit switch
		{
			AtcUnit.ClearanceDelivery => "DELIVERY", 
			AtcUnit.Ground => "GROUND", 
			AtcUnit.Tower => (context.DepartureRunway != null) ? "TOWER_DEPARTURE" : "TOWER_ARRIVAL", 
			AtcUnit.Departure => "DEPARTURE", 
			AtcUnit.Center => "CENTER", 
			AtcUnit.Arrival => "ARRIVAL", 
			AtcUnit.Approach => "APPROACH", 
			_ => "CLEARANCE", 
		};
		if (1 == 0)
		{
		}
		string controller_role = text;
		string clearance_type = DetermineClearanceType(context);
		object permissions = GetPermissions(context);
		var state_flags = new
		{
			ifr_clearance_issued = !string.IsNullOrWhiteSpace(context.SquawkCode),
			taxi_clearance_issued = false,
			lineup_issued = false,
			takeoff_issued = false,
			approach_issued = (context.ArrivalVectors?.ClearedForApproach ?? false),
			landing_issued = false
		};
		string? selectedSID = context.SelectedSID;
		string? selectedSTAR = context.SelectedSTAR;
		string? selectedApproachName = context.SelectedApproachName;
		SidSelectionResult? selectedSid = context.SelectedSid;
		int via_radar_vectors;
		if (selectedSid == null || selectedSid.Mode != ProcedureSelectionMode.Vectors)
		{
			StarSelectionResult? selectedStar = context.SelectedStar;
			via_radar_vectors = ((selectedStar != null && selectedStar.Mode == ProcedureSelectionMode.Vectors) ? 1 : 0);
		}
		else
		{
			via_radar_vectors = 1;
		}
		var anon = new
		{
			sid = selectedSID,
			star = selectedSTAR,
			approach = selectedApproachName,
			via_radar_vectors = ((byte)via_radar_vectors != 0)
		};
		var weather_relevant = new
		{
			dep_wind_dir = context.OriginWeather?.WindDirectionDegrees,
			dep_wind_kt = context.OriginWeather?.WindSpeedKnots,
			arr_wind_dir = context.DestinationWeather?.WindDirectionDegrees,
			arr_wind_kt = context.DestinationWeather?.WindSpeedKnots,
			qnh_hpa_dep = 1013,
			qnh_hpa_arr = 1013
		};
		var flight_info = new
		{
			callsign = context.Callsign,
			aircraft_type = (context.Aircraft?.IcaoType ?? "B738"),
			dep_icao = context.OriginIcao,
			dep_name = GetAirportName(context.OriginIcao),
			arr_icao = context.DestinationIcao,
			arr_name = GetAirportName(context.DestinationIcao),
			cruise_level = $"FL{context.CruiseFlightLevel}",
			alternate_icao = (string)null
		};
		var clearance_decision = new
		{
			clearance_type = clearance_type,
			cleared_to = GetAirportName(context.DestinationIcao),
			route_summary = GetRouteSummary(context),
			dep_runway = context.SelectedDepartureRunway,
			arr_runway = context.SelectedArrivalRunway,
			sid = context.SelectedSID,
			star = context.SelectedSTAR,
			approach = context.SelectedApproachName,
			via_radar_vectors = anon.via_radar_vectors,
			initial_altitude_ft = GetInitialAltitude(context),
			cleared_altitude_ft = context.ClearedAltitude,
			cleared_heading_deg = context.ClearedHeading,
			speed_restriction_kt = (int?)null,
			squawk = context.SquawkCode
		};
		return new { controller_role, phase, flight_info, clearance_decision, weather_relevant, state_flags, permissions };
	}

	private static string DetermineClearanceType(FlightContext context)
	{
		FlightPhase currentPhase = context.CurrentPhase;
		if (1 == 0)
		{
		}
		string result = currentPhase switch
		{
			FlightPhase.Preflight_Clearance => "IFR_CLEARANCE", 
			FlightPhase.Taxi_Out => "TAXI", 
			FlightPhase.Lineup_Takeoff => (context.DepartureRunway != null) ? "LINEUP" : "INFORMATION_ONLY", 
			FlightPhase.Climb_Departure => "CLIMB", 
			FlightPhase.Descent_Arrival => "DESCENT", 
			FlightPhase.Approach => "APPROACH", 
			FlightPhase.Landing => "LANDING", 
			_ => "INFORMATION_ONLY", 
		};
		if (1 == 0)
		{
		}
		return result;
	}

	private static string GetRouteSummary(FlightContext context)
	{
		EnrouteRoute? enrouteRoute = context.EnrouteRoute;
		if (enrouteRoute != null && enrouteRoute.WaypointIdentifiers.Count > 0)
		{
		IReadOnlyList<string> waypointIdentifiers = context.EnrouteRoute!.WaypointIdentifiers;
		if (waypointIdentifiers.Count <= 5)
		{
			return string.Join(" ", waypointIdentifiers);
		}
		return waypointIdentifiers[0] + " ... " + waypointIdentifiers[waypointIdentifiers.Count - 1];
		}
		return "as filed";
	}

	private static int? GetInitialAltitude(FlightContext context)
	{
		if (context.ClearedAltitude.HasValue)
		{
			return context.ClearedAltitude.Value;
		}
		if (context.CruiseFlightLevel > 300)
		{
			return 5000;
		}
		return 3000;
	}

	private static string GetAirportName(string icao)
	{
		if (1 == 0)
		{
		}
		string result = icao switch
		{
			"GMMN" => "Casablanca", 
			"ELLX" => "Luxembourg", 
			"EDDM" => "Munich", 
			"LOWI" => "Innsbruck", 
			_ => icao, 
		};
		if (1 == 0)
		{
		}
		return result;
	}

	private static object GetPermissions(FlightContext context)
	{
		return new
		{
			allow_ifr_clearance = (context.CurrentPhase == FlightPhase.Preflight_Clearance),
			allow_taxi = (context.CurrentPhase == FlightPhase.Taxi_Out || context.CurrentPhase == FlightPhase.Taxi_In),
			allow_lineup = (context.CurrentPhase == FlightPhase.Lineup_Takeoff && context.DepartureRunway != null),
			allow_takeoff_clearance = (context.CurrentPhase == FlightPhase.Lineup_Takeoff && context.DepartureRunway != null),
			allow_approach_clearance = (context.CurrentPhase == FlightPhase.Approach || context.CurrentPhase == FlightPhase.Descent_Arrival),
			allow_landing_clearance = (context.CurrentPhase == FlightPhase.Landing && context.ArrivalRunway != null)
		};
	}
}
