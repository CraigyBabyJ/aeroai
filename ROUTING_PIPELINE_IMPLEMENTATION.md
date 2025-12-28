# Intent Router + Logging Pipeline Implementation

## Summary

Implemented a robust routing pipeline that ensures procedural phrases (radio checks) are handled by hard-coded rules first, with all unmatched-but-usable transcripts falling back to the LLM. Added comprehensive structured logging and metrics tracking for routing decisions.

## Files Created

1. **`AeroAI/Atc/TranscriptUsabilityChecker.cs`**
   - Validates if transcripts are usable for LLM processing
   - Checks length, token count, filler words, and STT confidence
   - Provides reasons for unusable transcripts

2. **`AeroAI/Atc/RoutingDecision.cs`**
   - Data structure for routing decision information
   - Contains transcript, intent, route taken, reason, callsign, etc.

3. **`AeroAI/Atc/RoutingMetrics.cs`**
   - Thread-safe in-memory metrics counter
   - Tracks: total transcripts, procedural hits, LLM calls, say again count, LLM failures, unusable transcripts
   - Provides snapshot with calculated rates

4. **`AeroAI/Atc/RoutingDecisionLogger.cs`**
   - Structured logging for routing decisions
   - Logs to debug pipeline with category "IntentRouter"
   - Logs metrics summary every 10 transcripts

5. **`AeroAI.Tests/TranscriptUsabilityCheckerTests.cs`**
   - Unit tests for transcript usability validation

6. **`AeroAI.Tests/RoutingDecisionTests.cs`**
   - Integration tests for routing behavior
   - Verifies LLM bypass for radio checks
   - Verifies LLM fallback for usable non-procedural transcripts
   - Verifies "say again" for unusable transcripts

## Files Modified

1. **`AeroAI/Atc/AeroAiLlmSession.cs`**
   - Added routing metrics (static instance)
   - Added usability checking before and after normalization
   - Added routing decision logging
   - Updated routing logic to:
     - Check procedural intents first (radio checks)
     - Route usable non-procedural transcripts to LLM
     - Only return "say again" for unusable transcripts or LLM failures
   - Updated `CallLlmAsync` to track metrics and log failures
   - Added `LogRoutingDecision` helper method
   - Exposed `GetRoutingMetrics()` static method

2. **`AeroAI/Atc/ProceduralIntentRouter.cs`**
   - Already implemented (from previous task)
   - Handles radio check detection and hard-coded responses

## Key Changes

### 1. Routing Priority (Fixed Order)

1. **Procedural Intents** (radio checks) → Hard-coded response, bypass LLM
2. **Session Manager** (if available) → Try session-based handling
3. **Pending States** (readback, confirmation) → Handle pending states
4. **Callsign Gating** → Prompt if callsign unknown
5. **Phase-Specific Routing** → Route to phase handlers
6. **LLM Fallback** → If usable and no match, route to LLM
7. **Say Again** → Only for unusable transcripts or LLM failures

### 2. Usability Heuristics

A transcript is considered **usable** if:
- Not empty or whitespace
- Length >= 3 characters
- Has >= 2 tokens (or 1 token that looks like a callsign)
- Not all tokens are filler words ("uh", "um", "test", etc.)
- STT confidence >= threshold (if provided)

### 3. Structured Logging

Every transcript processed emits a routing decision log entry:
```
[IntentRouter] route=LLM intent=None usable=True reason="No match → LLM fallback" transcript="request clearance"
```

Logs include:
- Raw and normalized transcript
- STT confidence (if available)
- Matched intent (RadioCheck / None)
- Route taken (Procedural / LLM / SayAgain)
- Reason for routing decision
- Extracted callsign (if any)
- Usability status and reason

### 4. Metrics Tracking

Thread-safe counters track:
- `TotalTranscripts`: All transcripts processed
- `ProceduralHits`: Matched by procedural intents
- `LlmCalls`: Number of LLM calls made
- `SayAgainCount`: "Say again" responses
- `LlmFailures`: LLM call failures
- `UnusableTranscripts`: Unusable transcripts

Metrics are logged every 10 transcripts to avoid spam:
```
[IntentRouter.Metrics] total=50 procedural=5 (10.0%) llm=40 (80.0%) say_again=5 (10.0%) failures=0
```

### 5. LLM Fallback Logic

**Before (Bug)**: Unmatched transcripts returned "say again" without calling LLM.

**After (Fixed)**: 
- If transcript is usable and not procedural → Route to LLM
- If LLM call fails → Return "say again" and log failure
- If transcript is unusable → Return "say again" without calling LLM

## Testing

### Unit Tests

1. **TranscriptUsabilityCheckerTests**
   - Valid transcripts return true
   - Unusable transcripts return false
   - Confidence threshold validation
   - Reason extraction for unusable transcripts

2. **RoutingDecisionTests**
   - Usable non-procedural → Routes to LLM
   - Unusable transcript → Returns "say again", no LLM call
   - Empty transcript → Returns "say again", no LLM call
   - Radio check → Bypasses LLM

### Integration

- Routing decision logs appear in debug panel (category: "IntentRouter")
- Metrics summary appears every 10 transcripts
- LLM failures are logged with error details

## Usage

### Accessing Metrics

```csharp
var metrics = AeroAiLlmSession.GetRoutingMetrics();
Console.WriteLine($"LLM calls: {metrics.LlmCalls} ({metrics.LlmCallRate:P1})");
```

### Viewing Logs

1. Enable debug mode in UI (Debug checkbox)
2. Open debug log panel
3. Look for `[IntentRouter]` category entries
4. Metrics appear every 10 transcripts as `[IntentRouter.Metrics]`

## Bug Fix

**Original Issue**: "Easy one two three radio check" was not passed to LLM and returned "say again".

**Root Cause**: No fallback routing to LLM for unmatched-but-usable transcripts.

**Fix**: Added explicit LLM fallback after all routing attempts if transcript is usable.

## Performance

- Metrics use `Interlocked` for thread-safe counting (minimal overhead)
- Logging is async via existing OnDebug pipeline
- Metrics summary logged every 10 transcripts (not every transcript)
- No new external dependencies

## Future Enhancements

- Extract STT confidence from STT service and pass to routing
- Add token/cost estimation to routing decisions
- Add more procedural intents (standby, say again, etc.)
- Add routing decision export/analysis tools

