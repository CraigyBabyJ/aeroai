# AeroAI ATC Phrase Engine

ATC phrase generator used by AeroAI.UI. Text output remains primary; VoiceLab handles TTS and voice selection. The UI now routes ATC text through `IAtcResponseGenerator` (OpenAI provider by default) and you can switch providers via `userconfig.json` (`AtcTextProvider`).

## Environment
```
OPENAI_API_KEY=sk-your-key-here
OPENAI_MODEL=gpt-4o-mini
OPENAI_BASE_URL=https://api.openai.com/v1
AEROAI_SYSTEM_PROMPT_PATH=prompts/aeroai_system_prompt.txt

```

## Prompt + validation
- Default system prompt path is `prompts/aeroai_finetuned_prompt.txt` (override with `AEROAI_SYSTEM_PROMPT_PATH` if you want `aeroai_system_prompt.txt` instead).
- `OpenAiAtcResponseGenerator` enforces `AtcResponseValidator` when `FlightContext` is provided in the `AtcRequest`. Invalid replies fall back to a deterministic clearance.
- `AeroAiPhraseEngine` is a legacy wrapper around the OpenAI generator and still honors the same validation and logging behavior.
- Readback flow uses `ReadbackValidator`/`ReadbackNormalizer` plus `SpokenNumberNormalizer` and `CallsignValidator` to keep transcripts consistent before hitting the LLM.
- Debug logging: `AEROAI_LOG_FILE` appends prompt/response blocks; `AEROAI_LOG_API` re-enables the verbose API request/response dump.

## VoiceLab profiles (regional voices)
- Profiles live in `voicelab/xtts_service/voices/<voice_id>/meta.json` and control `engine`, `roles`, and `region_codes` for auto selection.
- AeroAI sends `voice_id="auto"` plus `role` and `facility_icao`; VoiceLab chooses a matching profile based on role + ICAO prefix.

## Usage
Run the AeroAI desktop UI and interact via the mic or text input. The phrase engine generates ATC responses; VoiceLab handles TTS while failures log warnings and never block text output.

## Host-side radio effect example
If you want a subtle radio effect and squelch tail for ATC audio (only ATC, not pilot), an external host can do:

```csharp
// 1) Get text from AeroAI (existing flow)
var atcResponseText = await atcSession.HandlePilotTransmissionAsync(pilotInput);

// 2) Convert text -> MP3 using your TTS engine
byte[] ttsMp3Bytes = await MyTtsEngine.SynthesizeAsync(atcResponseText);

// 3) Apply radio FX (optional, only if enabled/profile exists)
byte[] withFx = AeroAI.Audio.RadioEffectProcessor.ApplyToAtcResponse(
    ttsMp3Bytes,
    flightContext.CurrentAtcUnit // ClearanceDelivery/Ground/Tower/etc.
);

// 4) Play/save the resulting MP3
await MyMp3Pipe.SendAsync(withFx);
```

Config: `Config/audio-effects.json` (keys match `AtcUnit` names, e.g., `ClearanceDelivery`) with bandpass/noise/compression/dryWetMix and optional `squelchTailFile`. If disabled or missing, audio is returned unchanged.
