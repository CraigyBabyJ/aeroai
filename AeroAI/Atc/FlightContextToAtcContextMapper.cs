using System;
using System.Collections.Generic;
using AeroAI.Models;
using AeroAI.Data;
using AeroAI.Config;

namespace AeroAI.Atc;

public static class FlightContextToAtcContextMapper
{
	public static AtcContext Map(FlightContext flightContext, bool ifrClearanceIssued = false, PilotIntent? pilotIntent = null, bool hideDestination = false)
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
		string depAirportName = AirportNameResolver.ResolveAirportName(flightContext.OriginIcao, flightContext);
		string arrAirportName = hideDestination
			? string.Empty
			: AirportNameResolver.ResolveAirportName(flightContext.DestinationIcao, flightContext);

		string? clearedTo = hideDestination ? null : arrAirportName;

		string? depRunway = StripRunwayPrefix(flightContext.SelectedDepartureRunway ?? flightContext.DepartureRunway?.RunwayIdentifier);
		int initialAltitude = flightContext.ClearedAltitude ?? GetDefaultInitialAltitude(flightContext);
		string routeSummary = GetRouteSummary(flightContext);

		string? squawk = flightContext.SquawkCode;

		bool dataReady = !string.IsNullOrWhiteSpace(clearedTo)
			&& !string.IsNullOrWhiteSpace(depRunway)
			&& initialAltitude > 0
			&& !string.IsNullOrWhiteSpace(squawk);

		bool clearanceAlreadyIssued = ifrClearanceIssued || flightContext.CurrentAtcState == AtcState.ClearanceIssued;
		string clearanceType = flightContext.CurrentPhase == FlightPhase.Preflight_Clearance && dataReady && !clearanceAlreadyIssued
			? "IFR_CLEARANCE"
			: "INFORMATION_ONLY";

		// If no departure runway was selected yet, and we're in preflight clearance, keep runway null
		// so the LLM will prompt for it instead of assuming/announcing one.
		if (flightContext.CurrentPhase == FlightPhase.Preflight_Clearance && flightContext.DepartureRunway == null)
		{
			depRunway = null;
			dataReady = !string.IsNullOrWhiteSpace(clearedTo)
				&& initialAltitude > 0
				&& !string.IsNullOrWhiteSpace(squawk);
			if (clearanceType == "IFR_CLEARANCE" && !dataReady)
			{
				clearanceType = "INFORMATION_ONLY";
			}
		}

		// Use RadioCallsign (spoken form) if available, otherwise fall back to Callsign or RawCallsign
		string callsign = !string.IsNullOrWhiteSpace(flightContext.RadioCallsign)
			? flightContext.RadioCallsign
			: (!string.IsNullOrWhiteSpace(flightContext.Callsign)
				? flightContext.Callsign
				: flightContext.RawCallsign);
		if (string.IsNullOrWhiteSpace(callsign))
		{
			callsign = "UNKNOWN";
		}

		FlightInfo flightInfo = new FlightInfo
		{
			Callsign = callsign,
			AircraftType = flightContext.Aircraft?.IcaoType ?? "B738",
			DepIcao = flightContext.OriginIcao,
			DepName = depAirportName,
			DepAirportName = depAirportName,
			ArrIcao = flightContext.DestinationIcao,
			ArrName = arrAirportName,
			ArrAirportName = arrAirportName,
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

		// Get current ATIS letter from cache (actual current letter, not just what pilot said).
		var currentAtis = AtisMetarCache.Get(flightContext.OriginIcao);
		var atisLetter = !string.IsNullOrWhiteSpace(currentAtis.AtisLetter)
			? currentAtis.AtisLetter
			: flightContext.DepartureAtisLetter;

		AtcContext ctx = new AtcContext
		{
			ControllerRole = controllerRole,
			Phase = phase,
			FlightInfo = flightInfo,
			ClearanceDecision = clearanceDecision,
			WeatherRelevant = weatherRelevant,
			StateFlags = stateFlags,
			Permissions = permissions,
			CallsignInfo = callsignInfo,
			DepartureAtisLetter = atisLetter
		};

		if (AirportFrequencies.TryGetGroundFrequency(flightContext.OriginIcao, out double groundFreq))
		{
			ctx.GroundFrequencyMhz = groundFreq;
		}

		// Apply phase defaults AFTER filling critical clearance fields.
		PhaseDefaults.ApplyPhaseDefaults(flightContext.CurrentPhase, ctx);
		return ctx;
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
		// Use SimBrief initial altitude if available, otherwise calculate default
		if (context.ClearedAltitude.HasValue && context.ClearedAltitude.Value > 0)
			return context.ClearedAltitude.Value;
		
		return context.CruiseFlightLevel > 300 ? 5000 : 3000;
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
