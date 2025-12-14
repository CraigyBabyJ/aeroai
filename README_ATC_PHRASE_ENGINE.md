# AeroAI ATC Phrase Engine

Console-based ATC phrase generator using OpenAI. Text output remains primary; optional TTS can be enabled and customized via voice profiles.

## Environment
```
OPENAI_API_KEY=sk-your-key-here
OPENAI_MODEL=gpt-4o-mini
OPENAI_BASE_URL=https://api.openai.com/v1
AEROAI_SYSTEM_PROMPT_PATH=prompts/aeroai_system_prompt.txt

# Optional TTS
AEROAI_TTS_ENABLED=false
# Overrides
AEROAI_TTS_MODEL=gpt-4o-mini-tts
AEROAI_TTS_VOICE=alloy
OPENAI_API_BASE=https://api.openai.com/v1
AEROAI_TTS_SPEED=1.0
AEROAI_VOICE_PROFILE=some_profile_id (optional override)
AEROAI_VOICE_GIBRALTAR=english|spanish (defaults to english)
```

## Voice profiles (regional voices)
- Profiles live in `voices/*.json` with fields: `id`, `display_name`, `tts_model`, `tts_voice`, `style_hint` (future use), `speaking_rate`, `region_codes`, `controller_types`.
- Use ICAO prefixes in `region_codes` to apply by country/region (e.g., `LX` applies to all LX* airports). Gibraltar samples use `region_codes: ["LX"]`.
- Gibraltar clearance picks `gibraltar_english` or `gibraltar_spanish` based on `AEROAI_VOICE_GIBRALTAR` (default english). You can override selection with `AEROAI_VOICE_PROFILE`.
- If no profile matches, it falls back to `voices/default.json` then env-based TTS settings.
- Early step toward region-based voices; extend by adding more JSON files in `voices/`.

## Usage
Run the console app, type pilot transmissions; ATC responds with text. If TTS is enabled, audio is synthesized using the selected voice profile; failures log warnings and never block text output.

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
