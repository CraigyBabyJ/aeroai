# Hard-Coded Structures Analysis

## Summary
The AeroAI codebase has a **flows.json** system designed to drive ATC session behavior, but there are significant hard-coded structures in `AeroAiLlmSession.cs` that should be using the flow system instead.

## Current Situation

### ✅ What IS Using flows.json
- `AtcSessionManager.TryHandleAsync()` - Called early in routing pipeline (line 139)
- Handles basic intent → action → template flow
- Uses `AtcFlowEngine` to resolve transitions from flows.json

### ❌ What IS Hard-Coded (Should Use flows.json)

#### 1. **Clearance State Machine** (`HandleClearanceAsync` method)
**Location**: `AeroAiLlmSession.cs` lines 813-1310

**Hard-coded states**:
- `AtcState.Idle` → `AtcState.IfrRequested`
- `AtcState.IfrRequested` → `AtcState.ClearancePendingData` / `AtcState.ClearanceCollectingTrainingData`
- `AtcState.ClearancePendingData` → `AtcState.ClearanceReady`
- `AtcState.ClearanceCollectingTrainingData` → `AtcState.ClearanceReady`
- `AtcState.ClearanceReady` → `AtcState.ClearanceIssued`
- `AtcState.ClearanceIssued` → (readback handling)

**What should be in flows.json**:
```json
{
  "id": "clearance",
  "sub_states": [
    {
      "id": "idle",
      "transitions": [
        {
          "intent": "REQUEST_IFR_CLEARANCE",
          "next_state": "ifr_requested",
          "atc_action": "VALIDATE_REQUEST"
        }
      ]
    },
    {
      "id": "ifr_requested",
      "transitions": [
        {
          "condition": "missing_info",
          "next_state": "collecting_data",
          "atc_action": "PROMPT_MISSING_INFO"
        },
        {
          "condition": "data_complete",
          "next_state": "ready",
          "atc_action": "PREPARE_CLEARANCE"
        }
      ]
    },
    {
      "id": "collecting_data",
      "transitions": [
        {
          "condition": "all_data_collected",
          "next_state": "ready"
        }
      ]
    },
    {
      "id": "ready",
      "transitions": [
        {
          "intent": "CONFIRM_READY",
          "next_state": "issued",
          "atc_action": "ISSUE_CLEARANCE"
        }
      ]
    },
    {
      "id": "issued",
      "transitions": [
        {
          "intent": "READBACK_CLEARANCE",
          "atc_action": "READBACK_CORRECT",
          "set_pending_handoff": true,
          "handoff": { "role": "ground" }
        }
      ]
    }
  ]
}
```

#### 2. **Phase Routing** (`RouteToPhaseHandlerAsync` method)
**Location**: `AeroAiLlmSession.cs` lines 768-811

**Hard-coded switch statement**:
```csharp
switch (_context.CurrentPhase)
{
    case FlightPhase.Preflight_Clearance:
        return await HandleClearanceAsync(...);
    case FlightPhase.Taxi_Out:
        return await PhaseHandlers.HandleTaxiOutPhase(...);
    case FlightPhase.Lineup_Takeoff:
        return await PhaseHandlers.HandleLineupTakeoffPhase(...);
    // ... etc
}
```

**What should be in flows.json**:
- Phase-to-handler mapping
- Phase transition conditions
- Phase-specific intent routing

#### 3. **Missing Info Prompts** (`BuildMissingInfoPrompt` method)
**Location**: `AeroAiLlmSession.cs` lines 1645-1700

**Hard-coded checks**:
- Destination missing → "say destination"
- Initial altitude missing → "say initial altitude"
- Aircraft type missing → "confirm aircraft type"
- ATIS missing → "confirm you have the latest information"

**What should be in flows.json**:
- Missing data conditions
- Prompt templates for each missing field
- Order of prompts (priority)

#### 4. **State Transitions Based on Sim State** (`UpdatePhaseFromSimState` method)
**Location**: `AeroAiLlmSession.cs` lines 1458-1511

**Hard-coded phase transitions**:
- Taxi_Out → Lineup_Takeoff (when on runway)
- Lineup_Takeoff → Climb_Departure (when airborne)
- Climb_Departure → Enroute (when at cruise altitude)
- Enroute → Descent_Arrival (when descending)
- Descent_Arrival → Approach (when below 10,000 ft)
- Approach → Landing (when on final)
- Landing → Taxi_In (when on ground)

**What should be in flows.json**:
- Sim state conditions for phase transitions
- Automatic phase progression rules

## Recommended Changes

### 1. Extend flows.json Structure
Add support for:
- **Sub-states** within phases (e.g., clearance has: idle, requested, collecting, ready, issued)
- **Conditions** (not just intents) for transitions
- **Missing data prompts** configuration
- **Sim state conditions** for automatic phase transitions

### 2. Refactor HandleClearanceAsync
- Move state machine logic to flows.json
- Use `AtcFlowEngine` to resolve clearance state transitions
- Keep only business logic (data validation, extraction) in C#

### 3. Refactor RouteToPhaseHandlerAsync
- Map phases to flow definitions
- Use flow engine for phase routing decisions
- Keep phase handlers as implementation details

### 4. Move Missing Info Logic to flows.json
- Define missing data conditions
- Define prompt templates
- Define prompt priority/order

## Current flows.json Coverage

✅ **Covered**:
- Basic intent → action mapping
- Handoff transitions
- Role-to-phase mapping
- Template selection

❌ **NOT Covered**:
- Clearance sub-states (Idle, IfrRequested, PendingData, Ready, Issued)
- Missing data collection flow
- Sim state-based phase transitions
- Readback handling flow
- Destination confirmation flow
- ATIS confirmation flow

## Priority for Migration

1. **High Priority**: Clearance state machine (most complex, most hard-coded)
2. **Medium Priority**: Missing info prompts (affects user experience)
3. **Low Priority**: Sim state transitions (can stay hard-coded for now)

