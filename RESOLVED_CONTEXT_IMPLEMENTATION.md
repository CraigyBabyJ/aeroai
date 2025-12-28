# ResolvedContext Enforcement Implementation

## Summary

Implemented end-to-end "ResolvedContext enforcement" for both airports and callsigns using authoritative data from SimBrief and airports.json. This ensures ATC output never speaks ICAO codes or mangled STT approximations when we have authoritative context.

## Changes Made

### 1. New Classes

#### `ResolvedContext.cs`
- Holds resolved callsign and airport data with spoken forms
- Tracks source of airport names (SimBrief vs airports.json)
- Properties: `CallsignRaw`, `CallsignSpoken`, `DepartureIcao`, `ArrivalIcao`, `DepartureSpoken`, `ArrivalSpoken`, `DepartureSource`, `ArrivalSource`

#### `ResolvedContextBuilder.cs`
- Builds `ResolvedContext` from `FlightContext`
- Uses SimBrief data as primary source
- Falls back to airports.json for airport names if SimBrief lacks city/name
- Converts flight numbers to digit-by-digit spoken form (e.g., "223" → "two two three")
- Extracts city names from full airport names (e.g., "Calgary International Airport" → "Calgary")

#### `OutputGuard.cs`
- Post-processing guardrails to scrub ICAO codes and raw callsigns from LLM output
- Replaces exact ICAO matches with spoken names
- Replaces raw callsigns (e.g., "ACA223") with spoken callsigns (e.g., "Air Canada two two three")
- Logs replacements for debugging

### 2. Enhanced Logging

#### `RoutingDecision.cs`
- Added fields: `SimbriefCallsign`, `SpokenCallsign`, `DepIcao`, `ArrIcao`, `DepSpoken`, `ArrSpoken`, `DepSource`, `ArrSource`

#### `RoutingDecisionLogger.cs`
- Updated to include resolved context fields in logs
- Shows source of airport names (simbrief vs airports.json)

### 3. Integration Points

#### `AeroAiLlmSession.cs`
- **HandlePilotTransmissionAsync**: Builds `ResolvedContext` once at start, passes through routing pipeline
- **LogRoutingDecision**: Updated to accept and log `ResolvedContext`
- **CallLlmAsync**: Accepts `ResolvedContext`, applies `OutputGuard` to scrub output
- **RouteToPhaseHandlerAsync**: Accepts `ResolvedContext`, applies `OutputGuard` to all phase handler results
- **HandleClearanceAsync**: Accepts `ResolvedContext`, passes to `CallLlmAsync`

#### `FlightContextToAtcContextMapper.cs`
- Updated to prefer `RadioCallsign` (spoken form) over raw callsign

#### `prompts/aeroai_system_prompt.txt`
- Enhanced instructions: "Never speak airport ICAO codes (e.g., EGCC/EGLL, CYYC, CYVR)"
- Added: "Never speak raw callsigns (e.g., 'ACA223'). Always use the spoken callsign form"

### 4. Tests

#### `ResolvedContextBuilderTests.cs`
- Tests SimBrief data resolution
- Tests airports.json fallback
- Tests flight number to spoken digit conversion
- Tests missing data handling

#### `OutputGuardTests.cs`
- Tests ICAO code replacement
- Tests callsign replacement
- Tests combined replacements
- Tests null context handling

## How It Works

### Flow

1. **Transcript Received** → `HandlePilotTransmissionAsync`
2. **Build ResolvedContext** → `ResolvedContextBuilder.Build(_context)`
   - Extracts callsign from SimBrief (ACA223)
   - Converts to spoken form (Air Canada two two three)
   - Resolves airport names (CYVR → Vancouver, CYYC → Calgary)
   - Tracks source (SimBrief vs airports.json)
3. **Routing** → Procedural → SessionManager → PhaseHandlers → LLM
4. **LLM Call** → `CallLlmAsync` with `ResolvedContext`
5. **Output Scrubbing** → `OutputGuard.ScrubOutput` replaces any ICAO codes or raw callsigns
6. **Logging** → `RoutingDecision` includes all resolved context fields

### Airport Resolution Priority

1. **SimBrief name** (from `FlightContext.OriginName` / `DestinationName`)
   - Extract city if full name (e.g., "Calgary International Airport" → "Calgary")
   - Source: "simbrief"
2. **airports.json lookup** (via `AirportNameResolver`)
   - Prefer municipality, fallback to full name
   - Source: "airports.json"
3. **ICAO fallback** (should not happen in practice)
   - Source: "icao_fallback"

### Callsign Resolution Priority

1. **SimBrief raw callsign** (e.g., "ACA223")
2. **Convert to spoken form**:
   - Use `RadioCallsign` if available (already in spoken form)
   - Otherwise: `AirlineName` + flight number converted to digits
   - Example: "Air Canada 223" → "Air Canada two two three"

## Manual Verification

### Test Case: "request IFR clearance to YYC Calgary"

1. **Load SimBrief flight plan**:
   - Callsign: ACA223
   - Origin: CYVR (Vancouver)
   - Destination: CYYC (Calgary)

2. **Speak**: "request IFR clearance to YYC Calgary"

3. **Expected behavior**:
   - Transcript normalized: "request IFR clearance to YYC Calgary"
   - ResolvedContext built:
     - CallsignRaw: "ACA223"
     - CallsignSpoken: "Air Canada two two three"
     - DepIcao: "CYVR"
     - DepSpoken: "Vancouver" (source: simbrief)
     - ArrIcao: "CYYC"
     - ArrSpoken: "Calgary" (source: simbrief)
   - Routing: Usable transcript → LLM
   - LLM output: Should say "Calgary" not "CYYC", "Air Canada two two three" not "ACA223"
   - OutputGuard: Scrubs any remaining ICAO codes or raw callsigns
   - Final ATC response: "Air Canada two two three, cleared to Calgary..."

4. **Debug logs should show**:
   ```
   [IntentRouter] route=LLM intent=None usable=True reason="No match → LLM fallback" transcript="request IFR clearance to YYC Calgary" spoken_callsign="Air Canada two two three" dep="Vancouver"(simbrief) arr="Calgary"(simbrief)
   [OutputGuard] Replaced arrival ICAO 'CYYC' with 'Calgary'
   ```

## Files Changed

### New Files
- `AeroAI/Atc/ResolvedContext.cs`
- `AeroAI/Atc/ResolvedContextBuilder.cs`
- `AeroAI/Atc/OutputGuard.cs`
- `AeroAI.Tests/ResolvedContextBuilderTests.cs`
- `AeroAI.Tests/OutputGuardTests.cs`

### Modified Files
- `AeroAI/Atc/RoutingDecision.cs` - Added resolved context fields
- `AeroAI/Atc/RoutingDecisionLogger.cs` - Enhanced logging with resolved context
- `AeroAI/Atc/AeroAiLlmSession.cs` - Integrated ResolvedContext and OutputGuard throughout routing pipeline
- `AeroAI/Atc/FlightContextToAtcContextMapper.cs` - Prefer RadioCallsign (spoken form)
- `prompts/aeroai_system_prompt.txt` - Enhanced instructions about ICAO codes and callsigns

## Key Features

✅ **Airport Enforcement**: Never speaks ICAO codes (CYYC, CYVR) - always uses spoken names (Calgary, Vancouver)
✅ **Callsign Enforcement**: Never speaks raw callsigns (ACA223) - always uses spoken form (Air Canada two two three)
✅ **Source Tracking**: Logs whether airport names came from SimBrief or airports.json
✅ **Output Guardrails**: Post-processing scrubs any ICAO codes or raw callsigns that slip through
✅ **Enhanced Logging**: Every routing decision includes resolved context for analysis
✅ **Fallback Support**: Gracefully handles missing SimBrief data by falling back to airports.json

## Testing

Run the unit tests:
```bash
dotnet test AeroAI.Tests --filter "FullyQualifiedName~ResolvedContextBuilderTests|FullyQualifiedName~OutputGuardTests"
```

Manual verification:
1. Load a SimBrief flight plan (e.g., ACA223 CYVR→CYYC)
2. Speak: "request IFR clearance to YYC Calgary"
3. Verify ATC response says "Calgary" not "CYYC"
4. Verify ATC response says "Air Canada two two three" not "ACA223"
5. Check debug logs for resolved context fields

