# Voice Lab

Standalone FastAPI service with a lightweight web UI for exercising XTTS voices without touching the AeroAI app. It exposes a small API surface for TTS, cache inspection, and phrase-pack prefetching, backed by SQLite indexing and on-disk WAV caching. English-only; accent comes from your reference clips.

## Layout
- `xtts_service/app.py` - FastAPI app with endpoints, static UI mount, and health.
- `xtts_service/tts_engine.py` - placeholder audio generator; swap in XTTS here.
- `xtts_service/cache.py` & `xtts_service/db.py` - cache hashing, sharded paths, SQLite index.
- `xtts_service/phrasepacks.py` - canned phrases for warming the cache.
- `xtts_service/radio.py` - available radio profiles + lightweight DSP applied server-side (clean, vhf, cockpit, tinny, plus optional vatsim / congested net flavors).
- `xtts_service/voices/` - drop voice folders with `meta.json` and `ref.wav`.
- `xtts_service/ui/` - single-page UI (`/static`) for local testing.
- `xtts_service/cache_audio/` - cached WAVs (sharded) and `xtts_service/data/cache.db`.

## Quickstart
```bash
./run.sh        # Linux/macOS; creates .venv, installs deps, serves on 127.0.0.1:8008
# or
./run.ps1       # Windows PowerShell; same behavior
# then open http://127.0.0.1:8008
```

The bundled `tts_engine.py` tries to load XTTS; if XTTS libs are missing it will fall back to a sine-wave placeholder and report the error in `/health` and the UI. Hook your model into `_load_xtts()` / `_synthesize_xtts()` when ready.

## API
- `GET /` - serves the UI.
- `GET /health` - `{model_loaded, cuda_available, cache_items, cache_bytes, engine}` (engine contains status and any load error).
- `GET /voices` - lists voice profiles from `xtts_service/voices/*/meta.json`, radio profiles, roles, and phrase sets.
- `POST /tts` - body `{text, voice_id, role?, language?, speed?, radio_profile?, format}`; returns `audio/wav`.
  - Headers (segment mode): `X-Cache-Mode: segment`, `X-Cache: HIT|MISS|MIXED`, `X-Cache-Segments`, `X-Cache-Hits`, `X-Segment-Delimiter`.
  - Headers (full mode): `X-Cache-Mode: full`, `X-Cache: HIT|MISS`, `X-Cache-Key`.
- `POST /prefetch` - body `{voice_id, role?, language?, phraseset, limit?, speed?}`; caches clean single-segment audio (no stitching, no radio).
- `GET /cache/stats` - cache count and bytes.
- `GET /cache/recent?limit=50` - recent cache entries.
- `POST /cache/clear` - optional cache clear (WAVs + SQLite rows).

## Caching rules
- Default cache mode is **segment**, optimized for ATC-style clipped playback:
  - Use `|` or new lines to create hard segment breaks.
  - Each segment is cached independently (role-aware).
- Normalization: trim text and collapse whitespace (per segment).
- Cache key: SHA256 of `model_version | voice_id | role | radio_profile | speed | language | normalized_text`.
- Files: `xtts_service/cache_audio/<voice_id>/<k0k1>/<k2k3>/<hash>.wav`.
- Index: `xtts_service/data/cache.db` table `tts_cache` with key, metadata, size, timestamps (`created_at`, `last_hit`), hit_count, and model_version.
- On cache hit: `last_hit` and `hit_count` are updated.

### Cache mode
Set `TTS_CACHE_MODE=segment|full` (default: `segment`).

- `segment`: split input into segments, cache per segment, stitch server-side, apply radio DSP once.
- `full`: legacy whole-utterance caching (previous behavior).

### Segmentation (segment mode)
- If text contains `|`: split on `|` (hard ATC breaks).
- Else if text contains new lines: split on new lines (hard ATC breaks).
- Else: split on `. ? ! ;` (punctuation kept with the segment).
- Any segment over 220 characters is further split at word boundaries into ~160–220 character chunks.

## Voice assets
- Example voice: `xtts_service/voices/uk_male_1/` with `meta.json`.
- To add a voice, create `xtts_service/voices/<voice_id>/` with `meta.json` and `ref.wav` (clean mono, 16-22 kHz, ~6-12s). Update `meta.json` fields like `name`, `description`, `language`, `gender`, and any tags.
- Accent comes from your reference clip; keep text inputs in English.
