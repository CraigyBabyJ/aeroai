# Procedural Intent Router Implementation

## Summary

Implemented a procedural intent router that detects radio checks and handles them with hard-coded logic **before** any LLM routing. This ensures radio checks are handled procedurally and never require LLM interpretation.

## Files Created/Modified

### New Files

1. **`AeroAI/Atc/ProceduralIntent.cs`**
   - Enum defining procedural intents (currently only `RadioCheck`)
   - Extensible for future procedural intents

2. **`AeroAI/Atc/ProceduralIntentResult.cs`**
   - Result class containing match status, intent type, response text, and extracted callsign
   - Used to communicate match results from the router

3. **`AeroAI/Atc/ProceduralIntentRouter.cs`**
   - Main router implementation
   - Detects radio check phrases with forgiving matching
   - Extracts callsigns (with spoken number normalization)
   - Generates hard-coded responses
   - Logs matches via debug callback

4. **`AeroAI.Tests/ProceduralIntentRouterTests.cs`**
   - Comprehensive unit tests for radio check detection
   - Tests various phrasings, callsign extraction, and edge cases

5. **`AeroAI.Tests/ProceduralIntentRouterIntegrationTests.cs`**
   - Integration test verifying LLM bypass
   - Uses spy pattern to confirm LLM is not called for radio checks

### Modified Files

1. **`AeroAI/Atc/AeroAiLlmSession.cs`**
   - Added `_onDebug` field to store debug callback
   - Integrated `ProceduralIntentRouter.TryMatch()` call **immediately after normalization** and **before any LLM routing**
   - Returns procedural response early if match found, bypassing all LLM code paths

## Implementation Details

### Detection Logic

- **Pattern Matching**: Uses regex to match "radio check", "mic check", "radio checking" (case-insensitive)
- **Filler Word Filtering**: Removes common filler words ("uh", "um", "please", "request") for better matching
- **Flexible Phrasing**: Matches radio check phrases regardless of position in transmission

### Callsign Extraction

1. **Spoken Number Normalization**: Converts "one two three" → "123" using existing `SpokenNumberNormalizer`
2. **Pattern Extraction**: Tries to extract callsign before or after "radio check" phrase
3. **Context Fallback**: Uses callsign from `FlightContext` if extraction fails
4. **Existing Utilities**: Leverages `CallsignMatcher.ExtractCallsign()` for robust extraction

### Response Generation

- **Hard-coded Templates**: 4 response templates with callsign, 4 without
- **Random Selection**: Randomly selects from templates for variety
- **Callsign Inclusion**: Includes callsign in response if available, otherwise generic response

### Integration Point

The router is called in `AeroAiLlmSession.HandlePilotTransmissionAsync()` at this critical point:

```csharp
// After normalization
pilotTransmission = PilotTransmissionNormalizer.Normalize(...);

// PROCEDURAL INTENT ROUTING: Check BEFORE any LLM routing
var proceduralResult = ProceduralIntentRouter.TryMatch(pilotTransmission, _context, _onDebug);
if (proceduralResult.Matched && !string.IsNullOrWhiteSpace(proceduralResult.ResponseText))
{
    _lastAtcResponse = proceduralResult.ResponseText;
    return proceduralResult.ResponseText;  // BYPASSES ALL LLM CODE
}

// Only reaches here if no procedural intent matched
// ... continues to session manager and LLM routing
```

## LLM Bypass Confirmation

The integration test (`ProceduralIntentRouterIntegrationTests`) uses a spy pattern to verify:

1. ✅ Radio check transmissions generate responses
2. ✅ Responses are procedural (contain expected phrases)
3. ✅ **LLM is NOT called** (spy generator's `WasCalled` remains false)
4. ✅ Call count is zero

This confirms the LLM bypass works correctly.

## Test Coverage

### Unit Tests (`ProceduralIntentRouterTests`)

- ✅ Matches various radio check phrasings
- ✅ Extracts callsign from spoken numbers ("easy one two three" → "Easy 123")
- ✅ Extracts callsign before/after phrase
- ✅ Handles missing callsign (generic response)
- ✅ Includes callsign in response when available
- ✅ Rejects non-radio-check phrases
- ✅ Ignores filler words
- ✅ Falls back to context callsign
- ✅ Response randomization
- ✅ Original transcript preservation

### Integration Tests (`ProceduralIntentRouterIntegrationTests`)

- ✅ Radio check bypasses LLM
- ✅ Generic response when no callsign
- ✅ LLM spy confirms no calls for radio checks

## Usage Example

**Input**: "Easy one two three radio check"

**Processing**:
1. Normalized: "Easy 123 radio check" (spoken numbers converted)
2. Pattern matched: "radio check" detected
3. Callsign extracted: "Easy 123"
4. Response generated: "Easy 123, loud and clear." (randomly selected)

**Output**: Response returned immediately, **LLM never called**

## Future Extensibility

The `ProceduralIntent` enum can be extended with additional procedural intents:
- `PositionReport`
- `Standby`
- `SayAgain`
- etc.

Each new intent would add a corresponding handler method in `ProceduralIntentRouter`.

## Notes

- All changes are **local and safe** - no breaking changes to existing code
- Router runs **before** session manager and LLM routing
- Uses existing normalization utilities (`SpokenNumberNormalizer`, `CallsignMatcher`)
- Debug logging integrated via `_onDebug` callback
- Responses are deterministic (hard-coded) but randomized for variety

