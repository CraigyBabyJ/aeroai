using AeroAI.Models;

namespace AeroAI.Atc;

public class FlightContext
{
	public string Callsign { get; set; } = string.Empty;

	public string RawCallsign { get; set; } = string.Empty;

	public string AirlineIcao { get; set; } = string.Empty;

	public string FlightNumber { get; set; } = string.Empty;

	public string AirlineName { get; set; } = string.Empty;

	public string AirlineFullName { get; set; } = string.Empty;

	public string CanonicalCallsign { get; set; } = string.Empty;

	public string RadioCallsign { get; set; } = string.Empty;

	public string OriginIcao { get; set; } = string.Empty;

	public string OriginName { get; set; } = string.Empty;

	public string DestinationIcao { get; set; } = string.Empty;

	public string DestinationName { get; set; } = string.Empty;

	public EnrouteRoute? EnrouteRoute { get; set; }

	public WeatherInfo? OriginWeather { get; set; } = null;

	public WeatherInfo? DestinationWeather { get; set; } = null;

	/// <summary>
	/// Last known ATIS information letter for departure (best-effort, from pilot transmissions).
	/// </summary>
	public string? DepartureAtisLetter { get; set; }

	/// <summary>
	/// Stand or gate number/identifier (e.g., "15", "A12", "B3").
	/// </summary>
	public string? Stand { get; set; }

	public AircraftPerformanceProfile? Aircraft { get; set; } = null;

	public FlightPhase CurrentPhase { get; set; } = FlightPhase.Preflight_Clearance;

	public AtcState CurrentAtcState { get; set; } = AtcState.Idle;

	public AtcUnit CurrentAtcUnit { get; set; } = AtcUnit.ClearanceDelivery;

	public NavRunwaySummary? DepartureRunway { get; set; }

	public NavRunwaySummary? ArrivalRunway { get; set; }

	public SidSelectionResult? SelectedSid { get; set; }

	public StarSelectionResult? SelectedStar { get; set; }

	public ApproachSelectionResult? SelectedApproach { get; set; }

	public string DepartureAirport => OriginIcao;

	public string ArrivalAirport => DestinationIcao;

	public string? SelectedDepartureRunway => DepartureRunway?.RunwayIdentifier;

	public string? SelectedArrivalRunway => ArrivalRunway?.RunwayIdentifier;

	public string? SelectedSID
	{
		get
		{
			SidSelectionResult? selectedSid = SelectedSid;
			return (selectedSid != null && selectedSid.Mode == ProcedureSelectionMode.Published && selectedSid.SelectedSid != null) ? selectedSid.SelectedSid.ProcedureIdentifier : null;
		}
	}

	public string? SelectedSTAR
	{
		get
		{
			StarSelectionResult? selectedStar = SelectedStar;
			return (selectedStar != null && selectedStar.Mode == ProcedureSelectionMode.Published && selectedStar.SelectedStar != null) ? selectedStar.SelectedStar.ProcedureIdentifier : null;
		}
	}

	public string? SelectedApproachName
	{
		get
		{
			ApproachSelectionResult? selectedApproach = SelectedApproach;
			return (selectedApproach != null && selectedApproach.Mode == ProcedureSelectionMode.Published && selectedApproach.SelectedApproach != null) ? selectedApproach.SelectedApproach.ProcedureIdentifier : null;
		}
	}

	public int? ClearedAltitude { get; set; }

	public int? ClearedHeading { get; set; }

	public string? SquawkCode { get; set; }

	public int CurrentAltitude { get; set; }

	public int CruiseFlightLevel { get; set; } = 330;

	public string? CurrentFrequency { get; set; }

	/// <summary>
	/// True when no ATC is available and the pilot should use UNICOM.
	/// </summary>
	public bool NoAtcAvailable { get; set; }

	public DepartureVectoringState? DepartureVectors { get; set; }

	public ArrivalVectoringState? ArrivalVectors { get; set; }

	/// <summary>
	/// Reset mutable state for a new flight/session.
	/// </summary>
	public void ResetForNewFlight()
	{
		Callsign = string.Empty;
		RawCallsign = string.Empty;
		AirlineIcao = string.Empty;
		FlightNumber = string.Empty;
		AirlineName = string.Empty;
		AirlineFullName = string.Empty;
		CanonicalCallsign = string.Empty;
		RadioCallsign = string.Empty;
		OriginName = string.Empty;
		DestinationName = string.Empty;
		DepartureAtisLetter = null;
		Stand = null;
		CurrentPhase = FlightPhase.Preflight_Clearance;
		CurrentAtcState = AtcState.Idle;
		CurrentAtcUnit = AtcUnit.ClearanceDelivery;
		DepartureRunway = null;
		ArrivalRunway = null;
		SelectedSid = null;
		SelectedStar = null;
		SelectedApproach = null;
		ClearedAltitude = null;
		ClearedHeading = null;
		SquawkCode = null;
		CurrentAltitude = 0;
		CurrentFrequency = null;
		DepartureVectors = null;
		ArrivalVectors = null;
	}
}
