namespace AeroAI.Atc;

public static class PhaseDefaults
{
	public static void ApplyPhaseDefaults(FlightPhase phase, AtcContext ctx)
	{
		switch (phase)
		{
		case FlightPhase.Preflight_Clearance:
			ctx.ControllerRole = "CLEARANCE";
			// Always allow clearance if data is complete, even if a previous flag says issued
			bool ready = ClearanceHelpers.ClearanceDataComplete(ctx);
			ctx.Permissions.AllowIfrClearance = ready;
			ctx.Permissions.AllowTaxi = false;
			ctx.Permissions.AllowLineup = false;
			ctx.Permissions.AllowTakeoffClearance = false;
			ctx.Permissions.AllowApproachClearance = false;
			ctx.Permissions.AllowLandingClearance = false;
			ctx.ClearanceDecision.ClearanceType = ready ? "IFR_CLEARANCE" : "INFORMATION_ONLY";
			break;
		case FlightPhase.Taxi_Out:
			ctx.ControllerRole = "GROUND";
			ctx.Permissions.AllowIfrClearance = false;
			ctx.Permissions.AllowTaxi = true;
			ctx.Permissions.AllowLineup = false;
			ctx.Permissions.AllowTakeoffClearance = false;
			ctx.Permissions.AllowApproachClearance = false;
			ctx.Permissions.AllowLandingClearance = false;
			break;
		case FlightPhase.Lineup_Takeoff:
			ctx.ControllerRole = "TOWER_DEPARTURE";
			ctx.Permissions.AllowIfrClearance = false;
			ctx.Permissions.AllowTaxi = false;
			ctx.Permissions.AllowLineup = true;
			ctx.Permissions.AllowTakeoffClearance = true;
			ctx.Permissions.AllowApproachClearance = false;
			ctx.Permissions.AllowLandingClearance = false;
			break;
		case FlightPhase.Climb_Departure:
			ctx.ControllerRole = "DEPARTURE";
			ctx.Permissions.AllowIfrClearance = false;
			ctx.Permissions.AllowTaxi = false;
			ctx.Permissions.AllowLineup = false;
			ctx.Permissions.AllowTakeoffClearance = false;
			ctx.Permissions.AllowApproachClearance = false;
			ctx.Permissions.AllowLandingClearance = false;
			break;
		case FlightPhase.Enroute:
			ctx.ControllerRole = "CENTER";
			ctx.Permissions.AllowIfrClearance = false;
			ctx.Permissions.AllowTaxi = false;
			ctx.Permissions.AllowLineup = false;
			ctx.Permissions.AllowTakeoffClearance = false;
			ctx.Permissions.AllowApproachClearance = false;
			ctx.Permissions.AllowLandingClearance = false;
			break;
		case FlightPhase.Descent_Arrival:
			ctx.ControllerRole = "CENTER";
			ctx.Permissions.AllowIfrClearance = false;
			ctx.Permissions.AllowTaxi = false;
			ctx.Permissions.AllowLineup = false;
			ctx.Permissions.AllowTakeoffClearance = false;
			ctx.Permissions.AllowApproachClearance = false;
			ctx.Permissions.AllowLandingClearance = false;
			break;
		case FlightPhase.Approach:
			ctx.ControllerRole = "APPROACH";
			ctx.Permissions.AllowIfrClearance = false;
			ctx.Permissions.AllowTaxi = false;
			ctx.Permissions.AllowLineup = false;
			ctx.Permissions.AllowTakeoffClearance = false;
			ctx.Permissions.AllowApproachClearance = true;
			ctx.Permissions.AllowLandingClearance = false;
			break;
		case FlightPhase.Landing:
			ctx.ControllerRole = "TOWER_ARRIVAL";
			ctx.Permissions.AllowIfrClearance = false;
			ctx.Permissions.AllowTaxi = false;
			ctx.Permissions.AllowLineup = false;
			ctx.Permissions.AllowTakeoffClearance = false;
			ctx.Permissions.AllowApproachClearance = false;
			ctx.Permissions.AllowLandingClearance = true;
			break;
		case FlightPhase.Taxi_In:
			ctx.ControllerRole = "GROUND_ARRIVAL";
			ctx.Permissions.AllowIfrClearance = false;
			ctx.Permissions.AllowTaxi = true;
			ctx.Permissions.AllowLineup = false;
			ctx.Permissions.AllowTakeoffClearance = false;
			ctx.Permissions.AllowApproachClearance = false;
			ctx.Permissions.AllowLandingClearance = false;
			break;
		default:
			ctx.ControllerRole = "CLEARANCE";
			ctx.Permissions.AllowIfrClearance = true;
			ctx.Permissions.AllowTaxi = false;
			ctx.Permissions.AllowLineup = false;
			ctx.Permissions.AllowTakeoffClearance = false;
			ctx.Permissions.AllowApproachClearance = false;
			ctx.Permissions.AllowLandingClearance = false;
			break;
		}
	}
}
