# Future Updates (Notes)

This file captures ideas and next steps that are **not implemented yet** but have been discussed for future iterations. It serves as a parking lot so we can revisit without losing context.

## VoIP / Radio Channel Layer
- Goal: shared frequency channels so multiple clients and ATC bots can talk/listen on the same freq (SayIntentions/VATSIM style).
- Approach:
  - Maintain a server-side map: `frequency -> set of client sessions`.
  - Clients (and ATC bots) publish transmissions tagged with `frequency`; server fans them out to all subscribers on that freq.
  - Start simple with serialized transmissions (no overlapping audio).
  - Later: true mixing + priority/occlusion rules if needed.
- Transport options:
  - **Phase 1:** text + stitched WAV fan-out (reuse current TTS output; minimal change).
  - **Phase 2:** live mic audio fan-out. Capture mono 16 kHz on client; send PCM/WAV chunks. Optionally add Opus once a codec dependency is available.
- Client changes:
  - Add PTT capture for mic, downmix to mono 16 kHz.
  - Retune API to join/leave frequency sets quickly.
  - Playback incoming audio for the tuned freq; optional radio DSP overlay for live voice.
- Server notes:
  - Keep XTTS/cache as-is; the fan-out layer is separate.
  - Optional Opus roundtrip (encode→decode at 16 kHz mono) after stitching to add VoIP coloration, gated by a flag/profile.
  - No user toggle if you want to “lock in” VoIP tone globally.

## Opus Integration (Optional)
- Not implemented. Would require adding an Opus-capable dependency (e.g., pyogg/opuslib or ffmpeg/libopus bindings).
- Intended hook: after stitching, resample to 16 kHz mono, Opus encode→decode, then radio DSP.
- Trade-offs: small CPU cost; more authentic VoIP artifacts; removes client-side ability to disable coloration.

## Additional Radio Profiles
- New profiles added: `vatsim`, `congested` (net-style grit, dropouts, wobble).
- To revert to original set, remove those entries from `xtts_service/radio.py` (noted inline).

