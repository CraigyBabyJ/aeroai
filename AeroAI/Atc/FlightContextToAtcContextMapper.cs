using System;
using System.Collections.Generic;
using AeroAI.Models;
using AeroAI.Data;

namespace AeroAI.Atc;

public static class FlightContextToAtcContextMapper
{
	public static AtcContext Map(FlightContext flightContext, bool ifrClearanceIssued = false, PilotIntent? pilotIntent = null)
	{
		string controllerRole = flightContext.CurrentAtcUnit switch
		{
			AtcUnit.ClearanceDelivery => "DELIVERY",
			AtcUnit.Ground => "GROUND",
			AtcUnit.Tower => flightContext.DepartureRunway != null ? "TOWER_DEPARTURE" : "TOWER_ARRIVAL",
			AtcUnit.Departure => "DEPARTURE",
			AtcUnit.Center => "CENTER",
			AtcUnit.Arrival => "ARRIVAL",
			AtcUnit.Approach => "APPROACH",
			_ => "DELIVERY"
		};

		string phase = flightContext.CurrentPhase switch
		{
			FlightPhase.Preflight_Clearance => "CLEARANCE",
			FlightPhase.Taxi_Out => "TAXI_OUT",
			FlightPhase.Lineup_Takeoff => flightContext.DepartureRunway != null ? "LINEUP" : "FINAL",
			FlightPhase.Climb_Departure => "CLIMB",
			FlightPhase.Enroute => "ENROUTE",
			FlightPhase.Descent_Arrival => "DESCENT",
			FlightPhase.Approach => "FINAL",
			FlightPhase.Landing => "FINAL",
			FlightPhase.Taxi_In => "TAXI_IN",
			_ => "CLEARANCE"
		};

		// Critical clearance fields
		string clearedTo = !string.IsNullOrWhiteSpace(flightContext.DestinationIcao)
			? GetAirportName(flightContext.DestinationIcao)
			: flightContext.DestinationIcao;

		string? depRunway = StripRunwayPrefix(flightContext.SelectedDepartureRunway ?? flightContext.DepartureRunway?.RunwayIdentifier);
		int initialAltitude = flightContext.ClearedAltitude ?? GetDefaultInitialAltitude(flightContext);
		string routeSummary = GetRouteSummary(flightContext);

		string? squawk = flightContext.SquawkCode;
		if (string.IsNullOrWhiteSpace(squawk))
		{
			squawk = GenerateSquawk();
			flightContext.SquawkCode = squawk;
		}

		bool dataReady = !string.IsNullOrWhiteSpace(clearedTo)
			&& !string.IsNullOrWhiteSpace(depRunway)
			&& initialAltitude > 0
			&& !string.IsNullOrWhiteSpace(squawk);

		bool clearanceAlreadyIssued = ifrClearanceIssued || flightContext.CurrentAtcState == AtcState.ClearanceIssued;
		string clearanceType = flightContext.CurrentPhase == FlightPhase.Preflight_Clearance && dataReady && !clearanceAlreadyIssued
			? "IFR_CLEARANCE"
			: "INFORMATION_ONLY";

		string callsign = !string.IsNullOrWhiteSpace(flightContext.Callsign)
			? flightContext.Callsign
			: flightContext.RawCallsign;
		if (string.IsNullOrWhiteSpace(callsign))
		{
			callsign = "UNKNOWN";
		}

		FlightInfo flightInfo = new FlightInfo
		{
			Callsign = callsign,
			AircraftType = flightContext.Aircraft?.IcaoType ?? "B738",
			DepIcao = flightContext.OriginIcao,
			DepName = GetAirportName(flightContext.OriginIcao),
			ArrIcao = flightContext.DestinationIcao,
			ArrName = GetAirportName(flightContext.DestinationIcao),
			CruiseLevel = $"FL{flightContext.CruiseFlightLevel}",
			AlternateIcao = null
		};

		CallsignContextInfo callsignInfo = CallsignMatcher.BuildContextInfo(flightContext);

		ClearanceDecision clearanceDecision = new ClearanceDecision
		{
			ClearanceType = clearanceType,
			ClearedTo = clearedTo,
			RouteSummary = routeSummary,
			DepRunway = depRunway,
			ArrRunway = flightContext.SelectedArrivalRunway,
			Sid = flightContext.SelectedSID,
			Star = flightContext.SelectedSTAR,
			Approach = flightContext.SelectedApproachName,
			ViaRadarVectors = (flightContext.SelectedSid?.Mode == ProcedureSelectionMode.Vectors)
				|| (flightContext.SelectedStar?.Mode == ProcedureSelectionMode.Vectors),
			InitialAltitudeFt = initialAltitude,
			ClearedAltitudeFt = flightContext.ClearedAltitude,
			ClearedHeadingDeg = flightContext.ClearedHeading,
			SpeedRestrictionKt = null,
			Squawk = squawk,
			CallsignInfo = callsignInfo
		};

		WeatherRelevant weatherRelevant = new WeatherRelevant
		{
			DepWindDir = flightContext.OriginWeather?.WindDirectionDegrees,
			DepWindKt = flightContext.OriginWeather?.WindSpeedKnots,
			ArrWindDir = flightContext.DestinationWeather?.WindDirectionDegrees,
			ArrWindKt = flightContext.DestinationWeather?.WindSpeedKnots,
			QnhHpa = 1013
		};

		StateFlags stateFlags = new StateFlags
		{
			IfrClearanceIssued = clearanceAlreadyIssued,
			TaxiClearanceIssued = flightContext.CurrentPhase == FlightPhase.Taxi_Out && flightContext.CurrentPhase != FlightPhase.Preflight_Clearance,
			LineupIssued = false,
			TakeoffIssued = false,
			ApproachIssued = flightContext.ArrivalVectors?.ClearedForApproach ?? false,
			LandingIssued = false
		};

		Permissions permissions = new Permissions
		{
			AllowIfrClearance = flightContext.CurrentPhase == FlightPhase.Preflight_Clearance && dataReady && !clearanceAlreadyIssued,
			AllowTaxi = false,
			AllowLineup = false,
			AllowTakeoffClearance = false,
			AllowApproachClearance = false,
			AllowLandingClearance = false
		};

		AtcContext ctx = new AtcContext
		{
			ControllerRole = controllerRole,
			Phase = phase,
			FlightInfo = flightInfo,
			ClearanceDecision = clearanceDecision,
			WeatherRelevant = weatherRelevant,
			StateFlags = stateFlags,
			Permissions = permissions,
			CallsignInfo = callsignInfo
		};

		if (AirportFrequencies.TryGetGroundFrequency(flightContext.OriginIcao, out double groundFreq))
		{
			ctx.GroundFrequencyMhz = groundFreq;
		}

		// Apply phase defaults AFTER filling critical clearance fields.
		PhaseDefaults.ApplyPhaseDefaults(flightContext.CurrentPhase, ctx);
		return ctx;
	}

	private static string GetAirportName(string icao)
	{
		return icao switch
		{
			"GMMN" => "Casablanca",
			"ELLX" => "Luxembourg",
			"EDDM" => "Munich",
			"LOWI" => "Innsbruck",
			"EGCC" => "Manchester",
			_ => icao
		};
	}

	private static string GetRouteSummary(FlightContext context)
	{
		EnrouteRoute? enrouteRoute = context.EnrouteRoute;
		if (enrouteRoute != null && enrouteRoute.WaypointIdentifiers.Count > 0)
		{
			IReadOnlyList<string> waypointIdentifiers = enrouteRoute.WaypointIdentifiers;
			if (waypointIdentifiers.Count <= 5)
			{
				return string.Join(" ", waypointIdentifiers);
			}
			return $"{waypointIdentifiers[0]} ... {waypointIdentifiers[^1]}";
		}

		return "as filed";
	}

	private static int GetDefaultInitialAltitude(FlightContext context)
	{
		return context.CruiseFlightLevel > 300 ? 5000 : 3000;
	}

	private static string GenerateSquawk()
	{
		var rng = new Random();
		return $"{rng.Next(1, 8)}{rng.Next(0, 8)}{rng.Next(0, 8)}{rng.Next(0, 8)}";
	}

	/// <summary>
	/// Strips "RW" prefix from runway identifiers so the model receives clean format like "24" or "27L"
	/// </summary>
	private static string? StripRunwayPrefix(string? runway)
	{
		if (string.IsNullOrWhiteSpace(runway))
			return runway;
		
		// Strip RW prefix if present (e.g., "RW24" -> "24", "RW27L" -> "27L")
		if (runway.StartsWith("RW", StringComparison.OrdinalIgnoreCase))
			return runway.Substring(2);
		
		return runway;
	}
}
