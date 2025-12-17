## AeroAI Clearance Delivery — Working Notes

### Current behaviour
- LLM prompt is in `prompts/aeroai_system_prompt.txt` (now includes readback-handling rules for CLEARANCE role when `state_flags.ifr_clearance_issued` is true).
- LLM call goes through `AeroAI/Llm/AeroAiPhraseEngine.cs`; it logs system prompt, user prompt, and raw response to console every turn, and also to the file set in `AEROAI_LOG_FILE` (if the env var is present).
- `AeroAI/Atc/AeroAiLlmSession.cs` routes readbacks after a clearance to the LLM (no longer returns null), so readback acknowledgments/corrections are spoken.
- `prompts/aeroai_system_prompt.txt` now: uses `route_summary` when not “as filed” (e.g., “via BARPA then flight plan route” or “via flight plan route” if as filed; includes radar vectors when flagged); explicitly says do NOT ask for readback (host handles).
- Ground frequency lookup: `Data/AirportFrequencies.cs` loads `Data/airport-frequencies.json` and `FlightContextToAtcContextMapper` injects `ground_frequency_mhz` when available; prompt allows adding a ground handoff on correct readback.
- Clearance gating: `PhaseDefaults` no longer blocks clearance when `ifr_clearance_issued` is true; if data is complete, `allow_ifr_clearance` stays true.
- Reset support: `FlightContext.ResetForNewFlight()` and `AeroAiLlmSession.ResetForNewFlight()` clear state/flags/runway/squawk/etc. for a new session.
- Voice/TTS: `Config/VoiceConfig` + `VoiceConfigLoader` (env-driven, TTS optional). `IAtcVoiceEngine` has an optional `VoiceProfile` parameter; `OpenAiAudioVoiceEngine` uses profile overrides (model/voice/speed) per call. Profiles load from `voices/*.json` via `VoiceProfileLoader`/`VoiceProfileManager`. Gibraltar override via `AEROAI_VOICE_GIBRALTAR` selects `gibraltar_english` or `gibraltar_spanish`; profiles use prefix `region_codes` (e.g., `LX`), `controller_types` to match CLEARANCE. Fallbacks: default profile then env config; logs warnings, never breaks text.

### Quick how-to
1) Run the app and watch the console for `=== ATC DEBUG PROMPT/RESPONSE ===` blocks. Set `AEROAI_LOG_FILE=logs/atc.log` if you want file logging.
2) First pilot call (no prior clearance): model issues clearance; `_state` moves to `ClearanceIssued`.
3) Second call (readback-style): context has `ifr_clearance_issued=true`; prompt readback block should return "readback correct" or corrections; may optionally add "contact ground when ready for push and start."
4) Push-to-talk: hold the mic button in the input bar, speak, release to auto-transcribe via local `whisper-cli.exe` (uses model under `./whisper/models/`, override via env if needed).

### Key files
- `prompts/aeroai_system_prompt.txt` - system prompt, now with readback rules.
- `AeroAI/Llm/AeroAiPhraseEngine.cs` - logging around LLM calls.
- `AeroAI/Atc/AeroAiLlmSession.cs` - state machine; readbacks now sent to LLM in `ClearanceIssued`.
- `AeroAI/Atc/FlightContext.cs` and `AeroAI/Atc/AeroAiLlmSession.cs` - reset methods for a fresh flight/session.
- `AeroAI/Atc/FlightContextToAtcContextMapper.cs` - injects ground frequency when available.
- `Data/AirportFrequencies.cs` - JSON-backed ground frequency lookup; `AtcNavDataDemo.csproj` copies the JSON to output. Use `Data/convert_frequencies_to_json.py` to convert CSV to JSON.
- `voices/` - voice profiles (`default`, `gibraltar_english`, `gibraltar_spanish`) with `region_codes` prefixes and `controller_types`; `VoiceProfileLoader`/`VoiceProfileManager` select profiles; `OpenAiAudioVoiceEngine` uses them.
- `AeroAI.UI/Services/WhisperSttService.cs` - push-to-talk mic capture + whisper-cli transcription (UI mic button in input bar, looks for whisper/whisper-cli.exe and whisper/models/ggml-medium.en-q5_0.bin).

### Env vars
- `AEROAI_LOG_FILE` - optional path to append prompt/response logs.
- `AEROAI_LOG_API` - if set to truthy, also triggers the older API logging block.
- TTS/voices: `AEROAI_TTS_ENABLED`, `OPENAI_API_KEY`, `AEROAI_TTS_MODEL`, `AEROAI_TTS_VOICE`, `AEROAI_TTS_SPEED`, `OPENAI_API_BASE`, `AEROAI_VOICE_PROFILE`, `AEROAI_VOICE_GIBRALTAR` (english/spanish).
- Whisper STT (UI push-to-talk): place `whisper-cli.exe` in `whisper/` and `ggml-medium.en-q5_0.bin` in `whisper/models/`.
-### Open issues / next steps
- Verify runway selection runs before clearance call; if `dep_runway` is missing in the prompt, model still asks for it. Add a debug print before mapping to ensure the picker populated `SelectedDepartureRunway`.
- Add a ground handoff after correct readback (prompt allows it when `ground_frequency_mhz` is present).
- Consider orchestrator/wrapper: enforce required slots, regen on missing fields, lock callsign/runway/route, and handle deterministic fallbacks.
- If needed, add a fallback to populate `SelectedDepartureRunway` from `DepartureRunway.RunwayIdentifier` when picker misses.

---

## Training Data & Fine-Tuning

### Training Dataset
- **Location:** `Data/training_aeroai.json`
- **Size:** ~22,000 examples
- **Format:** OpenAI Chat/JSONL

```json
{"messages":[
  {"role":"system","content":"You are AeroAI ATC."},
  {"role":"user","content":"{context_json}\nPILOT: pilot message"},
  {"role":"assistant","content":"ATC response"}
]}
```

### Context Fields
| Field | Description | Example |
|-------|-------------|---------|
| `controller_role` | DELIVERY, GROUND, TOWER, DEPARTURE, CENTER, APPROACH | `"DELIVERY"` |
| `callsign` | Aircraft callsign | `"BAW456"` |
| `clearance_type` | IFR or VFR | `"IFR"` |
| `cleared_to` | Destination ICAO | `"EGLL"` |
| `sid` | SID procedure or null | `"LAM3F"` |
| `dep_runway` / `arrival_runway` | Runway identifiers | `"27R"` |
| `initial_altitude` | Initial climb altitude | `"5000"` |
| `squawk` | Transponder code | `"4271"` |
| `approach_type` | ILS, RNAV, VISUAL | `"ILS"` |

### Scenarios Covered
- IFR/VFR clearances, readback correct/incorrect
- Push and start, taxi with routes
- Line up and takeoff, landing and vacate
- Go around, approach vectors (ILS/RNAV/Visual)
- Frequency handoffs, MAYDAY/PAN PAN emergencies
- TCAS RA, weather deviations, holdings

### Data Quality Scripts
| Script | Purpose |
|--------|---------|
| `Data/clean_training_data.py` | Remove duplicates and low-quality entries |
| `Data/populate_templates.py` | Fill in placeholder templates |
| `Data/generate_training.py` | Generate additional training data |

### Fine-Tuning Cost (OpenAI)
| Examples | Tokens | Training Cost |
|----------|--------|---------------|
| 22,000 | ~4.4M | ~$40-45 (one-time) |

After fine-tuning: Model ID format `ft:gpt-4o-mini-2024-07-18:org::xxxxx`, can drop most system prompt.

---

## Alternative Architectures

### Option 1: Current (All OpenAI)
```
Pilot → OpenAI LLM → OpenAI TTS → Audio
Cost: ~$15-35/month (moderate use)
```

---

## Local LLM Options

## Local LLM Options

| Model | Size | VRAM Needed | Quality |
|-------|------|-------------|---------|
| Llama 3.1 8B | 8B | 12GB+ | ⭐⭐⭐⭐ |
| Mistral 7B | 7B | 14GB+ | ⭐⭐⭐⭐ |
| Phi-3 Mini | 3.8B | 8GB+ | ⭐⭐⭐ |

### Ollama Setup
```bash
curl -fsSL https://ollama.com/install.sh | sh
ollama pull llama3.1:8b
```

Training data in `Data/training_aeroai.json` works with both OpenAI and local LLMs (minor format conversion for ChatML/Alpaca).

---

## Hardware Requirements (Full Local)

| Tier | GPU | Response Time | Cost |
|------|-----|---------------|------|
| Budget | RTX 3060 12GB | ~4-6 sec | ~$500-800 |
| **Recommended** | RTX 4070 12GB | ~2-4 sec | ~$800-1200 |
| Best | RTX 4090 24GB | ~1-2 sec | ~$1500-2500 |

---

## Cost Comparison (200 requests/day)

| Setup | LLM | TTS | Monthly Total |
|-------|-----|-----|---------------|
| All OpenAI | ~$1.50 | ~$14 | ~$15.50 |
| OpenAI LLM + Local TTS | ~$1.50 | $0 | ~$1.50 |
| All Local | $0 | $0 | $0 |

---

## Audio Pipeline (Current Implementation)

### Flow
```
OpenAI TTS (WAV) → Radio Effects → Squelch Tail → NAudio Playback
```

### Key Audio Files
| File | Purpose |
|------|---------|
| `Audio/OpenAiAudioVoiceEngine.cs` | Main TTS engine, calls OpenAI API, applies effects |
| `Audio/TtsPlayback.cs` | WAV/MP3 playback via NAudio |
| `Audio/AtcAudioEffectProcessor.cs` | Applies DSP effects to WAV bytes |
| `Audio/RadioEffectProcessor.cs` | In-memory WAV processing, squelch tail |
| `Audio/RadioEffectSampleProvider.cs` | NAudio ISampleProvider for bandpass/noise/compression |
| `Config/audio-effects.json` | Effect settings per controller type |
| `Assets/Audio/delivery-tail.wav` | Squelch tail sound (radio key-off) |

### OpenAI TTS Configuration
- **Format:** WAV (not MP3) - avoids re-encoding latency
- **Request:** `response_format = "wav"`, `Accept: audio/wav`
- **Sample Rate:** 24kHz (OpenAI default)
- **Voice profiles** pass `instructions` parameter for consistent style

### Radio Effects (per controller type)
```json
// Config/audio-effects.json
{
  "ClearanceDelivery": {
    "bandpassLowHz": 400,
    "bandpassHighHz": 2800,
    "noiseLevel": 0.30,
    "compressionAmount": 0.50,
    "dryWetMix": 0.85,
    "squelchTailFile": "Assets/Audio/delivery-tail.wav"
  }
}
```

| Controller | Quality | Rationale |
|------------|---------|-----------|
| Delivery | Best | Close range, good equipment |
| Ground | Good | Airport proximity |
| Tower | Good | Line of sight |
| Departure | Medium | Increasing distance |
| Center | Worst | Long range, atmospheric |
| Approach | Medium | Returning, improving |

### Effect Processing Chain
1. **Bandpass filter** - Removes lows/highs (radio frequency response)
2. **Soft compression** - Limits dynamic range (radio AGC simulation)
3. **Noise injection** - Adds static/hiss (atmospheric noise)
4. **Dry/wet mix** - Blends original with processed
5. **Squelch tail** - Appends "kssht" sound at end

### WAV Processing Notes
- OpenAI sends streaming WAVs (dataLength = 0xFFFFFFFF)
- `RadioEffectProcessor` handles this by calculating from buffer size
- All processing is **in-memory** (no temp files) for speed
- Manual WAV header parsing for reliable format detection

---

## Voice Profiles

### Location
`voices/*.json` - One file per voice profile

### Example Profile (`atc_uk_delivery_female.json`)
```json
{
  "id": "atc_uk_delivery_female",
  "display_name": "UK Delivery (Female)",
  "tts_model": "gpt-4o-mini-tts",
  "voice": "nova",
  "speaking_rate": 1.1,
  "style_hint": "Speak in a calm, professional British female air traffic controller voice...",
  "region_codes": ["EG"],
  "controller_types": ["DELIVERY"]
}
```

### Profile Selection Logic
1. Match by `region_codes` (ICAO prefix, e.g., "EG" for UK)
2. Match by `controller_types` (DELIVERY, GROUND, TOWER, etc.)
3. Fall back to `default.json`

### Key Fields
| Field | Purpose |
|-------|---------|
| `tts_model` | OpenAI model (e.g., `gpt-4o-mini-tts`) |
| `voice` | OpenAI voice ID (`nova`, `alloy`, `echo`, etc.) |
| `speaking_rate` | Speed multiplier (1.0 = normal, 1.1 = slightly fast) |
| `style_hint` | Passed to OpenAI `instructions` for consistent accent/style |
| `region_codes` | ICAO prefixes this profile applies to |
| `controller_types` | Controller roles this profile applies to |

---

## Recent Changes Summary

### Audio System
- ✅ OpenAI TTS now requests **WAV** (not MP3) for lower latency
- ✅ Radio effects applied via `AtcAudioEffectProcessor`
- ✅ Squelch tail (`delivery-tail.wav`) appended after each response
- ✅ Effect intensity varies by controller type (Delivery=best, Center=worst)
- ✅ All audio processing is **in-memory** (no temp files)
- ✅ `style_hint` passed to OpenAI for consistent voice

### Voice Profiles
- ✅ Profile system loads from `voices/*.json`
- ✅ Profiles selected by region (ICAO prefix) + controller type
- ✅ `speaking_rate` fixed at 1.1 (was 1.7, too fast)
- ✅ `controller_role` uses "DELIVERY" (not "CLEARANCE")

### LLM & Training
- ✅ Training data: ~22,000 examples in `Data/training_aeroai.json`
- ✅ Format: OpenAI Chat/JSONL (works for fine-tuning)
- ✅ Covers all controller roles, scenarios, edge cases
- ✅ Data cleaning scripts available

### Bug Fixes
- ✅ "Radio check" detection now uses flexible matching (not exact string)
- ✅ Runway matching handles "RW" prefix differences (SimBrief vs NavData)
- ✅ SID extraction from route string when not in dedicated field
- ✅ WAV streaming format (dataLength=-1) handled correctly

---

## Key Notes
- **OpenAI is NOT local** - requires internet, has ongoing costs
- **Fine-tuned small model > generic large model** for specialized domain (ATC)
- **Easy to switch TTS later** (~30 min change)
- **Can always retrain** with more data as needed
- **Training data format** works for both cloud and local fine-tuning
- **Audio effects are controller-specific** - realism varies by ATC unit
- **WAV preferred over MP3** - faster, no decode overhead
