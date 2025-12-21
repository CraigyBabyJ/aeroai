# XTTS Service Notes

## Overview
VoiceLab serves XTTS and Coqui VITS (VCTK) voices from a single FastAPI backend. Engine selection is driven by `voices/*/meta.json` and is transparent to API clients.

## Engines and voices
- XTTS voices: use `engine: "xtts"` (or omit `engine` for backward compatibility) and require `voices/<id>/reference.wav` (or `ref.wav`).
- Coqui VITS voices: use `engine: "coqui_vits"`, `model: "tts_models/en/vctk/vits"`, and `speaker_id: "p###"`. No `ref.wav` is required.
- Starter pack: Coqui VCTK voice metas are preloaded as `voices/coqui_p225`, `coqui_p226`, …, `coqui_p243`.

## Windows: espeak-ng requirement
Coqui VITS on Windows needs an espeak backend. You have two options:
1) Install `espeak-ng` system-wide so `espeak-ng` (or `espeak`) is on `PATH`.
2) Drop `espeak-ng.exe` into `xtts_service/tools/espeak-ng/` (or `espeak.exe` into `xtts_service/tools/espeak/`) and restart the service.

The service will auto-detect either approach and report status in `/health` under the `espeak_*` fields. If espeak is missing, Coqui requests return a clear 500 error instead of the placeholder beep.

## ATC text normalization
Before segmentation and caching, ATC text is normalized while preserving `|` and newline delimiters. Rules include:
- Acronyms: `QNH`, `ATIS`, `ILS`, `VOR`, `NDB`, `RNAV`, `SID`, `STAR`, `IFR`, `VFR`, `CTAF`, `UNICOM` -> spaced letters.
- Runways: `runway 05R` -> `runway zero five right`.
- Headings: `heading 50` -> `heading zero five zero` (only after heading/hdg/turn).
- Squawk: `squawk 462` -> `squawk zero four six two`.
- Flight levels: `FL350` -> `flight level three five zero`.
- QNH/QFE digits: `QNH 1016` -> `Q N H one zero one six`.
- Frequencies: `121.800` -> `one two one decimal eight`.

When normalization changes the text, `/tts` responses include `X-Normalized: 1`.

## Pause controls and segmentation
- UI sends `hard_pause_ms` and `soft_pause_ms` on `/tts` for cadence control.
- Pauses apply only in segment/stitch mode (not full-cache mode).
- Hard pauses are used after `|` or newline boundaries; soft pauses are used for other boundaries or long-segment splits.
- Defaults: hard = 70 ms, soft = 35 ms. Pause values are not part of cache keys.

## Audio stitching and radio processing
- Segment outputs are trimmed for leading/trailing silence before stitching.
- Stitching inserts the requested pauses with a short crossfade to avoid clicks.
- Radio profiles are applied once after stitching; DSP stages clamp to int16 to prevent overflows.

## Health fields
`/health` reports XTTS and Coqui readiness plus `mode` (`multi`, `xtts_only`, `coqui_only`, or `placeholder`). Coqui and espeak status appear under:
- `coqui_loaded`, `coqui_error`, `coqui_model_name`
- `espeak_found`, `espeak_source`, `espeak_exe`

## UI
The voice dropdown labels include engine suffixes (e.g., `(xtts)` / `(coqui_vits)`), and an engine filter allows narrowing the list without refetching.
