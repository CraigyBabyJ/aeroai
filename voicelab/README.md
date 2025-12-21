# Voice Lab - XTTS Service

Standalone FastAPI service with a lightweight web UI for exercising XTTS voices without touching the AeroAI app. It exposes a comprehensive API surface for TTS, cache inspection, and phrase-pack prefetching, backed by SQLite indexing and on-disk WAV caching. English-only; accent comes from your reference clips.

## Architecture Overview

The service is built as a modular FastAPI application with the following components:

- **FastAPI App** (`app.py`) - Main application with REST endpoints, static UI mount, and health checks
- **TTS Engine** (`tts_engine.py`) - XTTS wrapper with Coqui TTS integration and sine-wave fallback
- **Cache System** (`cache.py` & `db.py`) - Cache hashing, sharded file paths, SQLite indexing with hit tracking
- **Voice Management** (`voices.py`) - Voice profile discovery and metadata handling
- **Radio DSP** (`radio.py`) - Available radio profiles + lightweight DSP applied server-side
- **Segmentation** (`segmentation.py`) - Text segmentation for ATC-style clipped playback
- **Audio Utils** (`audio_utils.py`) - WAV stitching, resampling, speed adjustment, format conversion
- **Phrase Packs** (`phrasepacks.py`) - Canned phrases for warming the cache
- **Web UI** (`ui/`) - Single-page UI (`/static`) for local testing

## File Structure

```
voicelab/
├── xtts_service/
│   ├── __init__.py              # Module exports
│   ├── app.py                   # FastAPI app with endpoints
│   ├── tts_engine.py            # XTTS wrapper (Coqui TTS integration)
│   ├── cache.py                 # Cache hashing and file management
│   ├── db.py                    # SQLite database operations
│   ├── voices.py                # Voice profile discovery
│   ├── radio.py                 # Radio DSP profiles and processing
│   ├── segmentation.py          # Text segmentation logic
│   ├── audio_utils.py           # Audio processing utilities
│   ├── phrasepacks.py           # Built-in phrase sets
│   ├── cache_audio/             # Cached WAVs (sharded by voice_id)
│   │   └── <voice_id>/
│   │       └── <k0k1>/<k2k3>/<hash>.wav
│   ├── data/
│   │   └── cache.db             # SQLite cache index
│   ├── voices/                  # Voice profiles directory
│   │   └── <voice_id>/
│   │       ├── meta.json        # Voice metadata
│   │       └── ref.wav          # Reference audio (6-12s, mono, 16-22kHz)
│   ├── ui/                      # Web UI (static files)
│   │   ├── index.html
│   │   ├── app.js
│   │   └── styles.css
│   └── tests/                   # Unit tests
├── requirements.txt             # Python dependencies
├── run.sh                       # Linux/macOS startup script
└── run.ps1                      # Windows PowerShell startup script
```

## Quickstart

```bash
# Linux/macOS
./run.sh        # Creates .venv, installs deps, serves on 127.0.0.1:8008

# Windows PowerShell
./run.ps1       # Same behavior

# Then open http://127.0.0.1:8008
```

The bundled `tts_engine.py` tries to load XTTS via Coqui TTS; if XTTS libraries are missing it will fall back to a sine-wave placeholder and report the error in `/health` and the UI.

## API Endpoints

### `GET /`
Serves the web UI (`index.html`).

**Response:** HTML file

---

### `GET /health`
Returns service health and status information.

**Response:**
```json
{
  "model_loaded": true,
  "cuda_available": false,
  "cache_mode": "segment",
  "cache_items": 42,
  "cache_bytes": 1048576,
  "engine": {
    "model_version": "xtts-placeholder-0.2",
    "model_loaded": true,
    "cuda_available": false,
    "engine_error": null,
    "xtts_available": true,
    "xtts_ready": true,
    "speed_supported": true,
    "mode": "xtts"
  }
}
```

**Fields:**
- `model_loaded`: Whether XTTS model is loaded
- `cuda_available`: Whether CUDA is available for GPU acceleration
- `cache_mode`: Current cache mode (`segment` or `full`)
- `cache_items`: Number of cached audio segments
- `cache_bytes`: Total size of cached audio in bytes
- `engine`: Detailed engine status including model version, errors, and capabilities

---

### `GET /voices`
Lists available voice profiles, radio profiles, roles, and phrase sets.

**Response:**
```json
{
  "voices": [
    {
      "id": "uk_male_1",
      "name": "UK Male 1",
      "description": "Neutral British English male voice",
      "language": "en-GB",
      "gender": "male",
      "roles": ["delivery", "ground", "tower", "approach"],
      "tags": ["british", "neutral", "atc"]
    }
  ],
  "radio_profiles": [
    {"id": "clean", "name": "Clean", "description": "Neutral, full-band voice."},
    {"id": "vhf", "name": "VHF", "description": "Narrow-band VHF radio with hiss and light grit."},
    {"id": "cockpit", "name": "Cockpit", "description": "Closed cockpit intercom flavor with low-mid focus."},
    {"id": "tinny", "name": "Tinny", "description": "High-passed intercom flavor with light static."},
    {"id": "vatsim", "name": "VATSIM-ish", "description": "Narrow, hissy net audio with light dropouts and squelch."},
    {"id": "congested", "name": "Congested Net", "description": "Heavier grit, packet-loss style dropouts, fast squelch tail."}
  ],
  "roles": ["delivery", "ground", "tower", "approach"],
  "phrasesets": [
    {"id": "atc_checkin", "description": "Generic ATC check-in and acknowledgement phrases.", "count": 10},
    {"id": "callouts", "description": "Cabin and cockpit callouts for quick demos.", "count": 10},
    {"id": "greetings", "description": "Friendly samples for casual demo playback.", "count": 7}
  ]
}
```

---

### `POST /tts`
Synthesizes text to speech and returns WAV audio.

**Request Body:**
```json
{
  "text": "Tower, this is Ghost Rider requesting a flyby.",
  "voice_id": "uk_male_1",
  "role": "tower",
  "language": "en",
  "speed": 1.0,
  "radio_profile": "vhf",
  "format": "wav",
  "pause_hard_ms": 90,
  "pause_soft_ms": 70
}
```

**Parameters:**
- `text` (required): Text to synthesize. Supports segmentation delimiters (`|` for hard breaks, newlines, or punctuation)
- `voice_id` (required): Voice profile ID from `/voices`
- `role` (optional): ATC role (e.g., "tower", "ground", "approach", "delivery")
- `language` (optional, default: "en"): Language code
- `speed` (optional, default: 1.0): Playback speed multiplier (0.5-1.5)
- `radio_profile` (optional): Radio DSP profile (see `/voices` for available profiles)
- `format` (optional, default: "wav"): Output format (currently only "wav" supported)
- `pause_hard_ms` (optional): Pause duration for hard breaks (segment delimiters) in milliseconds
- `pause_soft_ms` (optional): Pause duration for soft breaks (wrapped segments) in milliseconds

**Response:** WAV audio bytes (`audio/wav`)

**Response Headers (Segment Mode):**
- `X-Cache-Mode: segment`
- `X-Cache: HIT|MISS|MIXED` - Cache status (MIXED means some segments cached, some not)
- `X-Cache-Segments: <count>` - Total number of segments
- `X-Cache-Hits: <count>` - Number of segments served from cache
- `X-Segment-Delimiter: pipe|newline|punct|none` - Delimiter mode used

**Response Headers (Full Mode):**
- `X-Cache-Mode: full`
- `X-Cache: HIT|MISS` - Cache status
- `X-Cache-Key: <hash>` - Cache key hash

**Error Responses:**
- `400`: Invalid request (unknown voice, invalid radio profile, empty text, unsupported format)
- `404`: Voice not found

---

### `POST /prefetch`
Prefetches phrases from a phrase set to warm the cache. Caches clean audio only (no stitching, no radio effects).

**Request Body:**
```json
{
  "voice_id": "uk_male_1",
  "role": "tower",
  "language": "en",
  "radio_profile": null,
  "phraseset": "atc_checkin",
  "speed": 1.0,
  "limit": 10
}
```

**Parameters:**
- `voice_id` (required): Voice profile ID
- `role` (optional): ATC role
- `language` (optional, default: "en"): Language code
- `radio_profile` (optional): Radio profile (ignored for prefetch, always uses clean)
- `phraseset` (required): Phrase set ID from `/voices`
- `speed` (optional, default: 1.0): Playback speed
- `limit` (optional): Maximum number of phrases to prefetch

**Response:**
```json
{
  "voice_id": "uk_male_1",
  "phraseset": "atc_checkin",
  "count": 10,
  "items": [
    {
      "text": "Tower, this is Ghost Rider requesting a flyby.",
      "key": "abc123...",
      "path": "xtts_service/cache_audio/uk_male_1/ab/c1/abc123....wav",
      "from_cache": false
    }
  ]
}
```

---

### `GET /cache/stats`
Returns cache statistics.

**Response:**
```json
{
  "items": 42,
  "bytes": 1048576
}
```

---

### `GET /cache/recent`
Returns recent cache entries, ordered by last hit time (or creation time if never hit).

**Query Parameters:**
- `limit` (optional, default: 50, max: 200): Maximum number of entries to return

**Response:**
```json
[
  {
    "key": "abc123...",
    "voice_id": "uk_male_1",
    "role": "tower",
    "radio_profile": null,
    "speed": 1.0,
    "language": "en",
    "text_norm": "Tower, this is Ghost Rider requesting a flyby.",
    "path": "xtts_service/cache_audio/uk_male_1/ab/c1/abc123....wav",
    "bytes": 24576,
    "created_at": "2024-01-01T12:00:00Z",
    "last_hit": "2024-01-01T12:05:00Z",
    "hit_count": 5,
    "model_version": "xtts-placeholder-0.2"
  }
]
```

---

### `POST /cache/clear`
Clears the entire cache (WAVs + SQLite rows).

**Response:**
```json
{
  "status": "cleared"
}
```

---

## Caching System

### Cache Modes

The service supports two cache modes, controlled by the `TTS_CACHE_MODE` environment variable:

- **`segment`** (default): Optimized for ATC-style clipped playback
  - Splits input text into segments
  - Caches each segment independently (role-aware)
  - Stitches segments server-side with configurable pauses
  - Applies radio DSP once to the final stitched audio
  - Better cache hit rates for repeated phrases

- **`full`**: Legacy whole-utterance caching
  - Caches complete text as single unit
  - Radio effects cached separately (clean audio cached first, then processed version)
  - Simpler but less efficient for repeated sub-phrases

### Cache Key Generation

Cache keys are SHA256 hashes of the following payload (pipe-separated):
```
model_version | voice_id | role | radio_profile | speed | language | normalized_text
```

**Normalization:**
- Text is trimmed and whitespace collapsed (per segment in segment mode)
- Empty segments are filtered out

### File Storage

Cached WAV files are stored in sharded directories:
```
cache_audio/<voice_id>/<k0k1>/<k2k3>/<hash>.wav
```

Where `k0k1` and `k2k3` are the first 2 and next 2 characters of the hash (for filesystem efficiency).

### Database Schema

SQLite table `tts_cache`:
- `key` (TEXT PRIMARY KEY): Cache key hash
- `voice_id` (TEXT): Voice profile ID
- `role` (TEXT): ATC role (nullable)
- `radio_profile` (TEXT): Radio profile (nullable)
- `speed` (REAL): Playback speed
- `language` (TEXT): Language code
- `text_norm` (TEXT): Normalized text
- `path` (TEXT): File system path to cached WAV
- `bytes` (INTEGER): File size in bytes
- `created_at` (TEXT): ISO timestamp of creation
- `last_hit` (TEXT): ISO timestamp of last cache hit (nullable)
- `hit_count` (INTEGER): Number of cache hits
- `model_version` (TEXT): Model version used for generation

**Cache Hit Tracking:**
- On cache hit: `last_hit` and `hit_count` are updated automatically
- Database migrations ensure columns exist (backward compatible)

---

## Text Segmentation

Segmentation is used in `segment` cache mode to split text into cacheable chunks.

### Segmentation Priority

1. **Pipe (`|`)**: Hard breaks (ATC-style)
   - Default pause: 90ms
   - Example: `"Cleared for takeoff|Runway two seven right"`

2. **Newlines**: Hard breaks
   - Default pause: 90ms
   - Example: `"Cleared for takeoff\nRunway two seven right"`

3. **Punctuation**: `. ? ! ;`
   - Default pause: 70ms
   - Punctuation is kept with the segment
   - Example: `"Cleared for takeoff. Runway two seven right."`

4. **None**: Single segment
   - No pauses

### Long Segment Splitting

If any segment exceeds 220 characters, it is further split at word boundaries into ~160-220 character chunks. Short trailing chunks (<160 chars) are merged with the previous chunk if the combined length stays under 220 characters.

### Pause Configuration

- `pause_hard_ms`: Pause duration for hard breaks (pipe/newline delimiters)
- `pause_soft_ms`: Pause duration for soft breaks (wrapped long segments)

Defaults:
- Hard breaks: 90ms
- Soft breaks: 70ms

---

## Radio Profiles

Radio profiles apply DSP effects to simulate various radio/intercom conditions.

### Available Profiles

1. **`clean`**: No processing (passthrough)
   - Full-band voice, neutral

2. **`vhf`**: VHF radio simulation
   - Narrow-band (320-2800 Hz)
   - Light hiss and static bursts
   - Bitcrushing and soft clipping
   - Squelch tail fade

3. **`cockpit`**: Cockpit intercom
   - Low-mid focus (200-2400 Hz)
   - Subtle noise and bursts
   - Moderate processing

4. **`tinny`**: High-passed intercom
   - High-frequency emphasis (520-3200 Hz)
   - Light static
   - Tinny character

5. **`vatsim`**: VATSIM-style net audio
   - Narrow band (320-3000 Hz)
   - Light dropouts and AM wobble
   - Hissy net audio character

6. **`congested`**: Congested net conditions
   - Heavier processing (360-2800 Hz)
   - Packet-loss style dropouts
   - Fast squelch tail
   - More aggressive grit

### DSP Processing Pipeline

1. **Bandpass filtering**: High-pass + low-pass (single-pole filters)
2. **Bitcrushing**: Reduces bit depth for digital artifacts
3. **Soft clipping**: Adds saturation
4. **Noise injection**: Hiss and static bursts
5. **Dropouts** (vatsim/congested): Random sample zeroing
6. **AM wobble** (vatsim/congested): Amplitude modulation
7. **Gain adjustment**: Profile-specific gain
8. **Peak normalization**: Normalizes to ~92% peak
9. **Tail fade**: Squelch tail fade-out

**DSP Version:** `RADIO_DSP_VERSION = "2"` (included in cache key for radio-processed audio)

---

## Voice Management

### Voice Directory Structure

Each voice is stored in `xtts_service/voices/<voice_id>/`:

```
voices/
└── <voice_id>/
    ├── meta.json    # Voice metadata (required)
    └── ref.wav      # Reference audio (required)
```

### Voice Metadata (`meta.json`)

```json
{
  "id": "uk_male_1",
  "name": "UK Male 1",
  "description": "Neutral British English male voice with even pacing.",
  "language": "en-GB",
  "gender": "male",
  "roles": ["delivery", "ground", "tower", "approach"],
  "tags": ["british", "neutral", "atc"]
}
```

**Fields:**
- `id` (auto-set from directory name): Unique voice identifier
- `name`: Display name
- `description`: Voice description
- `language`: Language code (e.g., "en", "en-GB", "en-US")
- `gender`: "male" or "female"
- `roles`: Array of ATC roles this voice supports
- `tags`: Array of descriptive tags

**Note:** Accent comes from the reference audio (`ref.wav`). Keep text inputs in English.

### Voice Selection Contract

- Clients send `voice_id="auto"` plus `role` and `facility_icao` (full ICAO).
- VoiceLab derives `region_prefix` from `facility_icao` and selects by `role` + region.
- XTTS voices win when present for a matching role/region; Coqui VITS is used only as a UK (EG/EI) fallback.

### Reference Audio Requirements

- **Format**: WAV, mono, 16-bit PCM
- **Sample Rate**: 16-22 kHz (recommended: 22050 Hz)
- **Duration**: 6-12 seconds
- **Content**: Clean speech sample in the desired accent/voice

### Adding a New Voice

1. Create directory: `xtts_service/voices/<voice_id>/`
2. Add `meta.json` with voice metadata
3. Add `ref.wav` with reference audio
4. Restart the service (or voices are auto-discovered on startup)

---

## TTS Engine

### XTTS Integration

The service uses Coqui TTS (XTTS v2) for synthesis:

- **Model**: `tts_models/multilingual/multi-dataset/xtts_v2` (configurable via `XTTS_MODEL_NAME` env var)
- **Device**: CUDA if available, otherwise CPU
- **Speed Control**: Native speed parameter if supported, otherwise WAV header resampling fallback

### Fallback Behavior

If XTTS libraries are not installed or model loading fails:
- Service remains runnable
- Returns sine-wave placeholder audio
- Reports error in `/health` endpoint
- UI displays error message

### Speed Control

1. **Native**: Tries `speed` parameter, then `speaking_rate` parameter
2. **Fallback**: Adjusts WAV sample rate (changes pitch, not ideal but functional)

Speed range: 0.5-1.5 (clamped)

---

## Audio Processing

### Audio Utilities (`audio_utils.py`)

**`wav_to_pcm16_mono(wav_bytes, target_sample_rate=None)`**
- Converts WAV to PCM16 mono
- Supports mono/stereo input
- Optional resampling to target sample rate
- Returns `(pcm_bytes, sample_rate)`

**`stitch_wavs(wav_chunks, pause_ms=140)`**
- Stitches multiple WAV chunks into one
- Converts all to PCM16 mono
- Resamples to first chunk's sample rate
- Inserts silence between segments
- Returns single WAV bytes

**`adjust_speed(wav_bytes, speed=1.0)`**
- Resamples PCM to change playback speed
- Changes pitch (not ideal, but functional)
- Speed range: 0.5-1.5

### Audio Format Requirements

- **Input**: WAV, mono or stereo, 16-bit PCM (preferred)
- **Output**: WAV, mono, 16-bit PCM, 22050 Hz (or source sample rate)
- **Processing**: All internal processing uses PCM16 mono

---

## Phrase Packs

Built-in phrase sets for cache warming:

- **`atc_checkin`**: Generic ATC check-in and acknowledgement phrases (10 phrases)
- **`callouts`**: Cabin and cockpit callouts (10 phrases)
- **`greetings`**: Friendly demo samples (7 phrases)

Phrase packs are defined in `phrasepacks.py` and can be extended programmatically.

---

## Dependencies

Core dependencies (`requirements.txt`):
- `fastapi>=0.110.0,<0.110.999` - Web framework
- `uvicorn[standard]>=0.24.0,<0.30.0` - ASGI server
- `aiofiles>=23.2.1` - Async file operations
- `pydantic>=2.7.0,<3.0.0` - Data validation

**Optional (for XTTS):**
- `TTS` (Coqui TTS) - Install separately: `pip install TTS`
- `torch` - PyTorch (for XTTS model)
- CUDA toolkit (for GPU acceleration)

---

## Environment Variables

- `TTS_CACHE_MODE`: Cache mode (`segment` or `full`, default: `segment`)
- `XTTS_MODEL_NAME`: XTTS model name (default: `tts_models/multilingual/multi-dataset/xtts_v2`)
- `PYTHONPATH`: Python path (set by run scripts)

---

## Development Notes

### Module Structure

- All modules use `from __future__ import annotations` for forward references
- Type hints throughout
- Error handling with graceful fallbacks
- Database migrations ensure backward compatibility

### Testing

Unit tests are in `xtts_service/tests/`:
- `test_audio_utils.py`: Audio processing tests
- `test_cache_key.py`: Cache key generation tests
- `test_segmentation.py`: Text segmentation tests

### Extending the Service

**Adding a new radio profile:**
1. Add entry to `RADIO_PROFILES` dict in `radio.py`
2. Add preset parameters to `presets` dict in `apply_radio_profile()`
3. Update `RADIO_DSP_VERSION` if DSP algorithm changes

**Adding a new phrase pack:**
1. Add entry to `library` dict in `phrasepacks.py`
2. Include `description` and `phrases` array

**Adding a new audio format:**
1. Extend `audio_utils.py` with format conversion
2. Update `app.py` to handle new format in `/tts` endpoint

---

## Future Integration Notes

This service is designed to be extended with additional TTS engines (e.g., Coqui TTS v3, other voice synthesis systems). The architecture separates:
- **Engine abstraction** (`tts_engine.py`) - Swap implementations here
- **Cache layer** (`cache.py`) - Works with any engine
- **API layer** (`app.py`) - Engine-agnostic endpoints

To add a new engine:
1. Implement `TtsEngine` interface (or create new engine class)
2. Update `app.py` to use new engine
3. Ensure engine returns `TtsResult` with `wav_bytes` and `sample_rate`

---

## Troubleshooting

**Service won't start:**
- Check Python version (3.8+)
- Verify dependencies installed: `pip install -r requirements.txt`
- Check port 8008 is available

**XTTS not working:**
- Install Coqui TTS: `pip install TTS`
- Check `/health` endpoint for error messages
- Verify CUDA if using GPU: `python -c "import torch; print(torch.cuda.is_available())"`

**Cache not working:**
- Check `cache_audio/` directory permissions
- Verify SQLite database is writable: `xtts_service/data/cache.db`
- Check disk space

**Voice not found:**
- Verify `xtts_service/voices/<voice_id>/meta.json` exists
- Verify `xtts_service/voices/<voice_id>/ref.wav` exists
- Check voice ID matches directory name

**Audio quality issues:**
- Ensure reference WAV is clean (6-12s, mono, 16-22kHz)
- Check radio profile settings
- Verify text normalization isn't corrupting input
