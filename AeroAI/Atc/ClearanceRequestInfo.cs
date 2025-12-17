using System;

namespace AeroAI.Atc;

/// <summary>
/// Tracks training slots collected during IFR clearance request (strict training mode).
/// </summary>
public sealed class ClearanceRequestInfo
{
	/// <summary>
	/// Callsign is known (required, session-sticky).
	/// </summary>
	public bool CallsignKnown { get; set; }

	/// <summary>
	/// Pilot explicitly requested IFR clearance (or "as filed IFR").
	/// </summary>
	public bool IfrRequestExplicit { get; set; }

	/// <summary>
	/// Destination confirmed (matches flight plan or pilot confirmed).
	/// </summary>
	public bool DestinationConfirmed { get; set; }

	/// <summary>
	/// Aircraft type stated/confirmed by pilot (even if SimBrief has it, strict mode requires pilot to state it).
	/// </summary>
	public bool AircraftTypeConfirmed { get; set; }

	/// <summary>
	/// Stand/Gate collected from pilot (e.g., "stand 15", "gate A12").
	/// </summary>
	public bool StandGateCollected { get; set; }

	/// <summary>
	/// Stand/Gate value (e.g., "15", "A12").
	/// </summary>
	public string? StandGateValue { get; set; }

	/// <summary>
	/// ATIS acknowledged by pilot ("with information X" OR "we have the latest information").
	/// </summary>
	public bool AtisAcknowledged { get; set; }

	/// <summary>
	/// Check if all required training slots are collected.
	/// </summary>
	public bool AllSlotsCollected => CallsignKnown && IfrRequestExplicit && DestinationConfirmed && 
	                                 AircraftTypeConfirmed && StandGateCollected && AtisAcknowledged;

	/// <summary>
	/// Reset for new clearance request.
	/// </summary>
	public void Reset()
	{
		IfrRequestExplicit = false;
		DestinationConfirmed = false;
		AircraftTypeConfirmed = false;
		StandGateCollected = false;
		StandGateValue = null;
		AtisAcknowledged = false;
		// CallsignKnown persists across turns
	}
}

