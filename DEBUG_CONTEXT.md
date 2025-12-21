# Debug: AI Making Things Up

## Problem
The AI is inventing data like "Luxembourg Clearance" and repeating full clearances instead of answering questions.

## Root Cause
The `AeroAiLlmSession` was using the old `PromptTemplate` which builds a different JSON structure than what the system prompt expects.

## Fix Applied

1. **Created `FlightContextToAtcContextMapper`** - Properly maps `FlightContext` → `AtcContext` with correct structure
2. **Updated `AeroAiLlmSession`** - Now uses `IAtcResponseGenerator` (OpenAI provider by default) which:
   - Loads the system prompt from file
   - Builds the correct JSON context structure
   - Tracks state flags (ifr_clearance_issued, etc.)

## What to Check

When you run the app, the AI should now receive:

```json
{
  "controller_role": "CLEARANCE",
  "phase": "CLEARANCE",
  "flight_info": {
    "callsign": "CJ",
    "aircraft_type": "B738",
    "dep_icao": "ELLX",
    "dep_name": "Luxembourg",  // ← This is now properly set
    "arr_icao": "GMMN",
    "arr_name": "Casablanca",
    "cruise_level": "FL350"
  },
  "clearance_decision": {
    "clearance_type": "IFR_CLEARANCE",
    "cleared_to": "Casablanca",
    "route_summary": "as filed",
    "dep_runway": "24",
    "initial_altitude_ft": 5000,
    "squawk": "4672"
  },
  "state_flags": {
    "ifr_clearance_issued": false,  // ← Tracks if clearance was already given
    ...
  },
  "permissions": {
    "allow_ifr_clearance": true,
    ...
  }
}
```

## Testing

After the fix:
1. First transmission: "CJ, cleared to Casablanca as filed, departure runway 24, initial climb 5000 feet, squawk 4672."
2. Second transmission (pilot asks about altitude): "CJ, initial altitude is five thousand feet." (NOT repeating the full clearance)

The state flag `ifr_clearance_issued` should be set to `true` after the first clearance, so the AI knows not to repeat it.



