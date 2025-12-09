## AeroAI Clearance Delivery — Working Notes

### Current behaviour
- LLM prompt is in `prompts/aeroai_system_prompt.txt` (now includes readback-handling rules for CLEARANCE role when `state_flags.ifr_clearance_issued` is true).
- LLM call goes through `AeroAI/Llm/AeroAiPhraseEngine.cs`; it logs system prompt, user prompt, and raw response to console every turn, and also to the file set in `AEROAI_LOG_FILE` (if the env var is present).
- `AeroAI/Atc/AeroAiLlmSession.cs` routes readbacks after a clearance to the LLM (no longer returns null), so readback acknowledgments/corrections are spoken.
- `prompts/aeroai_system_prompt.txt` now: uses `route_summary` when not “as filed” (e.g., “via BARPA then flight plan route” or “via flight plan route” if as filed; includes radar vectors when flagged); explicitly says do NOT ask for readback (host handles).
- Ground frequency lookup: `Data/AirportFrequencies.cs` loads `Data/airport-frequencies.csv` and `FlightContextToAtcContextMapper` injects `ground_frequency_mhz` when available; prompt allows adding a ground handoff on correct readback.
- Clearance gating: `PhaseDefaults` no longer blocks clearance when `ifr_clearance_issued` is true; if data is complete, `allow_ifr_clearance` stays true.
- Reset support: `FlightContext.ResetForNewFlight()` and `AeroAiLlmSession.ResetForNewFlight()` clear state/flags/runway/squawk/etc. for a new session.

### Quick how-to
1) Run the app and watch the console for `=== ATC DEBUG PROMPT/RESPONSE ===` blocks. Set `AEROAI_LOG_FILE=logs/atc.log` if you want file logging.
2) First pilot call (no prior clearance): model issues clearance; `_state` moves to `ClearanceIssued`.
3) Second call (readback-style): context has `ifr_clearance_issued=true`; prompt readback block should return “readback correct” or corrections; may optionally add “contact ground when ready for push and start.”

### Key files
- `prompts/aeroai_system_prompt.txt` — system prompt, now with readback rules.
- `AeroAI/Llm/AeroAiPhraseEngine.cs` — logging around LLM calls.
- `AeroAI/Atc/AeroAiLlmSession.cs` — state machine; readbacks now sent to LLM in `ClearanceIssued`.
- `AeroAI/Atc/FlightContext.cs` and `AeroAI/Atc/AeroAiLlmSession.cs` — reset methods for a fresh flight/session.
- `AeroAI/Atc/FlightContextToAtcContextMapper.cs` — injects ground frequency when available.
- `Data/AirportFrequencies.cs` — CSV-backed ground frequency lookup; `AtcNavDataDemo.csproj` copies the CSV to output.

### Env vars
- `AEROAI_LOG_FILE` — optional path to append prompt/response logs.
- `AEROAI_LOG_API` — if set to truthy, also triggers the older API logging block.

### Open issues / next steps
- Verify runway selection runs before clearance call; if `dep_runway` is missing in the prompt, model still asks for it. Add a debug print before mapping to ensure the picker populated `SelectedDepartureRunway`.
- Add a ground handoff after correct readback (prompt allows it when `ground_frequency_mhz` is present).
- Consider orchestrator/wrapper: enforce required slots, regen on missing fields, lock callsign/runway/route, and handle deterministic fallbacks.
- If needed, add a fallback to populate `SelectedDepartureRunway` from `DepartureRunway.RunwayIdentifier` when picker misses.
