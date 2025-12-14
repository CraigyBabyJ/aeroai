using System.Collections.Generic;
using AeroAI.Atc.Vectoring;
using AeroAI.Data;
using AeroAI.Logic;
using AeroAI.Models;

namespace AeroAI.Atc;

public class AeroAiSession
{
	private readonly INavDataRepository _navDataRepo;

	private readonly IRunwaySelector _runwaySelector;

	private readonly IProcedureSelector _procedureSelector;

	private readonly FlightContext _context;

	private readonly AtcResponseGenerator _responseGenerator;

	private readonly PilotIntentParser _intentParser;

	private readonly DepartureVectorGenerator _departureVectorGenerator;

	private readonly ArrivalVectorGenerator _arrivalVectorGenerator;

	private readonly IWaypointResolver _waypointResolver;

	private SimState? _lastSimState;

	public AeroAiSession(INavDataRepository navDataRepo, IRunwaySelector runwaySelector, IProcedureSelector procedureSelector, string originIcao, string destinationIcao, EnrouteRoute? enrouteRoute, WeatherInfo originWeather, WeatherInfo destinationWeather, AircraftPerformanceProfile aircraft, string callsign, IWaypointResolver? waypointResolver = null)
	{
		_navDataRepo = navDataRepo;
		_runwaySelector = runwaySelector;
		_procedureSelector = procedureSelector;
		_responseGenerator = new AtcResponseGenerator();
		_intentParser = new PilotIntentParser();
		_waypointResolver = waypointResolver ?? new StubWaypointResolver();
		_departureVectorGenerator = new DepartureVectorGenerator();
		_arrivalVectorGenerator = new ArrivalVectorGenerator();
		_context = new FlightContext
		{
			Callsign = callsign,
			RawCallsign = callsign,
			OriginIcao = originIcao,
			DestinationIcao = destinationIcao,
			EnrouteRoute = enrouteRoute,
			OriginWeather = originWeather,
			DestinationWeather = destinationWeather,
			Aircraft = aircraft,
			CurrentPhase = FlightPhase.Preflight_Clearance,
			CurrentAtcUnit = AtcUnit.ClearanceDelivery
		};
		InitializeFlightContext();
	}

	public string HandlePilotTransmission(string pilotText, SimState? simState = null)
	{
		if (string.IsNullOrWhiteSpace(pilotText))
		{
			return "Say again?";
		}
		if (simState != null)
		{
			_lastSimState = simState;
			_context.CurrentAltitude = simState.AltitudeFeet;
		}
		PilotIntent intent = _intentParser.ParseIntent(pilotText, _context);
		UpdateContextFromIntent(intent);
		if (_lastSimState != null)
		{
			string? text = TryGenerateVectoringResponse(intent);
			if (text != null)
			{
				return text;
			}
		}
		return _responseGenerator.GenerateResponse(intent, _context);
	}

	public void UpdateSimState(SimState simState)
	{
		_lastSimState = simState;
		_context.CurrentAltitude = simState.AltitudeFeet;
	}

	public FlightContext GetContext()
	{
		return _context;
	}

	private void InitializeFlightContext()
	{
		IReadOnlyList<NavRunwaySummary> runways = _navDataRepo.GetRunways(_context.OriginIcao);
		IReadOnlyList<NavRunwaySummary> runways2 = _navDataRepo.GetRunways(_context.DestinationIcao);
		if (runways.Count > 0)
		{
			RunwaySelectionResult runwaySelectionResult = _runwaySelector.SelectDepartureRunway(_context.OriginIcao, _context.OriginWeather, _context.Aircraft, runways);
			_context.DepartureRunway = runwaySelectionResult.SelectedRunway;
		}
		if (runways2.Count > 0)
		{
			RunwaySelectionResult runwaySelectionResult2 = _runwaySelector.SelectArrivalRunway(_context.DestinationIcao, _context.DestinationWeather, _context.Aircraft, runways2);
			_context.ArrivalRunway = runwaySelectionResult2.SelectedRunway;
		}
	}

	private void UpdateContextFromIntent(PilotIntent intent)
	{
		switch (intent.Type)
		{
		case IntentType.RequestClearance:
			if (_context.CurrentPhase == FlightPhase.Preflight_Clearance && _context.DepartureRunway != null)
			{
				IReadOnlyList<SidSummary> sids = _navDataRepo.GetSids(_context.OriginIcao);
				SidSelectionResult sidSelectionResult = _procedureSelector.SelectSidForRoute(_context.OriginIcao, _context.DepartureRunway, _context.EnrouteRoute ?? new EnrouteRoute
				{
					OriginIcao = _context.OriginIcao,
					DestinationIcao = _context.DestinationIcao
				}, sids);
				_context.SelectedSid = sidSelectionResult;
				if (sidSelectionResult.Mode == ProcedureSelectionMode.Vectors)
				{
					_context.DepartureVectors = new DepartureVectoringState();
				}
			}
			break;
		case IntentType.ReadbackClearance:
			_context.CurrentPhase = FlightPhase.Taxi_Out;
			_context.CurrentAtcUnit = AtcUnit.Ground;
			break;
		case IntentType.RequestPush:
			break;
		case IntentType.ReadyToTaxi:
			break;
		case IntentType.ReadyForDeparture:
			_context.CurrentPhase = FlightPhase.Lineup_Takeoff;
			_context.CurrentAtcUnit = AtcUnit.Tower;
			break;
		case IntentType.AcknowledgeTakeoff:
			_context.CurrentPhase = FlightPhase.Climb_Departure;
			_context.CurrentAtcUnit = AtcUnit.Departure;
			break;
		case IntentType.ContactDeparture:
			break;
		case IntentType.ClimbAcknowledged:
			if (_context.CurrentAltitude >= _context.CruiseFlightLevel - 10)
			{
				_context.CurrentPhase = FlightPhase.Enroute;
				_context.CurrentAtcUnit = AtcUnit.Center;
			}
			break;
		case IntentType.ContactArrival:
			_context.CurrentPhase = FlightPhase.Descent_Arrival;
			_context.CurrentAtcUnit = AtcUnit.Arrival;
			if (_context.ArrivalRunway != null)
			{
				IReadOnlyList<StarSummary> stars = _navDataRepo.GetStars(_context.DestinationIcao);
				StarSelectionResult starSelectionResult = _procedureSelector.SelectStarForRoute(_context.DestinationIcao, _context.ArrivalRunway, _context.EnrouteRoute ?? new EnrouteRoute
				{
					OriginIcao = _context.OriginIcao,
					DestinationIcao = _context.DestinationIcao
				}, stars);
				_context.SelectedStar = starSelectionResult;
				IReadOnlyList<ApproachSummary> approaches = _navDataRepo.GetApproaches(_context.DestinationIcao);
				ApproachSelectionResult selectedApproach = _procedureSelector.SelectApproachForRunway(_context.DestinationIcao, _context.ArrivalRunway, _context.DestinationWeather, approaches, starSelectionResult);
				_context.SelectedApproach = selectedApproach;
				if (starSelectionResult.Mode == ProcedureSelectionMode.Vectors)
				{
					_context.ArrivalVectors = new ArrivalVectoringState();
				}
			}
			break;
		case IntentType.RunwayInSight:
			_context.CurrentPhase = FlightPhase.Approach;
			_context.CurrentAtcUnit = AtcUnit.Tower;
			break;
		case IntentType.AcknowledgeLanding:
			_context.CurrentPhase = FlightPhase.Taxi_In;
			_context.CurrentAtcUnit = AtcUnit.Ground;
			break;
		case IntentType.RequestShutdown:
			_context.CurrentPhase = FlightPhase.Complete;
			break;
		}
	}

	private string? TryGenerateVectoringResponse(PilotIntent intent)
	{
		if (_lastSimState == null)
		{
			return null;
		}
		if (_context.CurrentPhase == FlightPhase.Climb_Departure && _context.DepartureVectors != null)
		{
			SidSelectionResult? selectedSid = _context.SelectedSid;
			if (selectedSid != null && selectedSid.Mode == ProcedureSelectionMode.Vectors)
			{
				VectorInstruction? vectorInstruction = _departureVectorGenerator.GenerateNextInstruction(_context, _lastSimState, _waypointResolver);
				if (vectorInstruction != null)
				{
					if (vectorInstruction.Heading.HasValue)
					{
						_context.ClearedHeading = vectorInstruction.Heading.Value;
					}
					if (vectorInstruction.Altitude.HasValue)
					{
						_context.ClearedAltitude = vectorInstruction.Altitude.Value;
					}
					return vectorInstruction.Phrase;
				}
			}
		}
		if ((_context.CurrentPhase == FlightPhase.Descent_Arrival || _context.CurrentPhase == FlightPhase.Approach) && _context.ArrivalVectors != null)
		{
			StarSelectionResult? selectedStar = _context.SelectedStar;
			if (selectedStar != null && selectedStar.Mode == ProcedureSelectionMode.Vectors)
			{
				VectorInstruction? vectorInstruction2 = _arrivalVectorGenerator.GenerateNextInstruction(_context, _lastSimState, _waypointResolver);
				if (vectorInstruction2 != null)
				{
					if (vectorInstruction2.Heading.HasValue)
					{
						_context.ClearedHeading = vectorInstruction2.Heading.Value;
					}
					if (vectorInstruction2.Altitude.HasValue)
					{
						_context.ClearedAltitude = vectorInstruction2.Altitude.Value;
					}
					return vectorInstruction2.Phrase;
				}
			}
		}
		return null;
	}
}
