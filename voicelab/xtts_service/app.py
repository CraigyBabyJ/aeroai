from __future__ import annotations

import os
from pathlib import Path
from typing import Dict, Optional

from fastapi import FastAPI, HTTPException, Response
from fastapi.responses import FileResponse
from fastapi.staticfiles import StaticFiles
from pydantic import BaseModel

from .cache import TtsCache
from .phrasepacks import PhrasePacks
from .radio import RADIO_DSP_VERSION, apply_radio_profile, list_radio_profiles, validate_radio_profile
from .segmentation import segment_text
from .tts_engine import TtsEngine
from .audio_utils import adjust_speed, wav_to_pcm16_mono
from .audio_edit import stitch_wavs, trim_silence_pcm16
from .normalize_atc import normalize_atc
from .voices import VoiceStore


DEFAULT_ROLES = ["delivery", "ground", "tower", "approach"]


BASE_DIR = Path(__file__).resolve().parent
UI_DIR = BASE_DIR / "ui"

engine = TtsEngine(BASE_DIR)
cache = TtsCache(base_dir=BASE_DIR, model_version="voicelab-multi")
voices = VoiceStore(base_dir=BASE_DIR)
phrasepacks = PhrasePacks()

app = FastAPI(title="XTTS Voice Lab", version="0.1.0")
app.mount("/static", StaticFiles(directory=UI_DIR), name="static")


class TtsRequest(BaseModel):
    text: str
    voice_id: str
    role: Optional[str] = None
    language: str = "en"
    speed: float = 1.0
    radio_profile: Optional[str] = None
    radio_intensity: Optional[float] = None
    format: str = "wav"
    soft_pause_ms: Optional[int] = None
    hard_pause_ms: Optional[int] = None
    pause_hard_ms: Optional[int] = None
    pause_soft_ms: Optional[int] = None


class PrefetchRequest(BaseModel):
    voice_id: str
    role: Optional[str] = None
    language: str = "en"
    radio_profile: Optional[str] = None
    phraseset: str
    speed: float = 1.0
    limit: Optional[int] = None


@app.get("/", response_class=FileResponse)
async def index():
    index_path = UI_DIR / "index.html"
    if not index_path.exists():
        raise HTTPException(status_code=500, detail="UI not found")
    return FileResponse(index_path)


@app.get("/health")
async def health():
    stats = cache.stats()
    return {
        "model_loaded": engine.model_loaded,
        "cuda_available": engine.cuda_available,
        "cache_mode": _get_cache_mode(),
        "cache_items": stats["items"],
        "cache_bytes": stats["bytes"],
        "engine": engine.health(),
    }


@app.get("/voices")
async def get_voices():
    return {
        "voices": voices.list_voices(),
        "radio_profiles": list_radio_profiles(),
        "roles": DEFAULT_ROLES,
        "phrasesets": phrasepacks.list_sets(),
    }


def _voice_engine(voice: Dict) -> str:
    return str(voice.get("engine") or "xtts").lower()


def _resolve_ref_wav(voice: Dict) -> Optional[Path]:
    if _voice_engine(voice) == "coqui_vits":
        return None
    voice_id = voice.get("id") or ""
    ref_wav = voices.get_ref_wav_path(voice_id)
    if not ref_wav:
        raise HTTPException(
            status_code=400,
            detail=f"Voice '{voice_id}' is missing ref.wav. Add xtts_service/voices/{voice_id}/ref.wav.",
        )
    return ref_wav


def _get_cache_mode() -> str:
    mode = (os.environ.get("TTS_CACHE_MODE") or "segment").strip().lower()
    return "full" if mode == "full" else "segment"


@app.post("/tts")
async def tts(body: TtsRequest):
    if body.format.lower() != "wav":
        raise HTTPException(status_code=400, detail="Only wav output is supported.")

    voice = voices.get(body.voice_id)
    if not voice:
        raise HTTPException(status_code=404, detail="Voice not found.")

    if body.radio_profile and not validate_radio_profile(body.radio_profile):
        raise HTTPException(status_code=400, detail="Unknown radio profile.")

    try:
        radio_intensity = 0.5 if body.radio_intensity is None else float(body.radio_intensity)
    except Exception:
        radio_intensity = 0.5
    radio_intensity = max(0.0, min(1.0, radio_intensity))

    normalized_atc, norm_changed = normalize_atc(body.text)
    ref_wav = _resolve_ref_wav(voice)
    voice_model_version = engine.model_version_for_voice(voice)

    cache_mode = _get_cache_mode()
    if cache_mode == "full":
        text_norm = cache.normalize(normalized_atc)
        if not text_norm:
            raise HTTPException(status_code=400, detail="Text is empty after normalization.")

        # Always cache a clean/dry render first so radio effects can reuse it.
        base_result = cache.get_or_generate(
            model_version=voice_model_version,
            voice_id=body.voice_id,
            role=body.role,
            radio_profile=None,
            speed=body.speed,
            language=body.language,
            text=text_norm,
            generator=lambda: engine.synthesize_for_voice(
                text=text_norm,
                voice=voice,
                speaker_wav=ref_wav,
                language=body.language,
                speed=body.speed,
            ).wav_bytes,
            return_audio=True,
        )

        if not body.radio_profile or body.radio_profile == "clean":
            result = base_result
        else:
            # Check if processed version exists; if not, derive from cached clean audio.
            processed = cache.get_or_generate(
                model_version=f"{voice_model_version}|radio_dsp:{RADIO_DSP_VERSION}|ri:{radio_intensity:.2f}",
                voice_id=body.voice_id,
                role=body.role,
                radio_profile=body.radio_profile,
                speed=body.speed,
                language=body.language,
                text=text_norm,
                generator=lambda: apply_radio_profile(
                    base_result["audio"],
                    profile=body.radio_profile or "clean",
                    radio_intensity=radio_intensity,
                ),
                return_audio=True,
            )
            result = processed

        headers = {
            "X-Cache-Mode": "full",
            "X-Cache": "HIT" if result.get("from_cache") else "MISS",
            "X-Cache-Key": result.get("key", ""),
        }
        if norm_changed:
            headers["X-Normalized"] = "1"
        return Response(content=result["audio"], media_type="audio/wav", headers=headers)

    hard_pause = body.hard_pause_ms if body.hard_pause_ms is not None else body.pause_hard_ms
    soft_pause = body.soft_pause_ms if body.soft_pause_ms is not None else body.pause_soft_ms
    if hard_pause is None:
        hard_pause = 70
    if soft_pause is None:
        soft_pause = 35

    seg = segment_text(
        normalized_atc,
        base_pause_ms=hard_pause,
        wrap_pause_ms=soft_pause,
    )
    if not seg.segments:
        raise HTTPException(status_code=400, detail="Text is empty after segmentation.")

    segment_results = []
    for segment in seg.segments:
        segment_results.append(
            cache.get_or_generate(
                model_version=voice_model_version,
                voice_id=body.voice_id,
                role=body.role,
                radio_profile=None,
                speed=body.speed,
                language=body.language,
                text=segment,
                generator=lambda s=segment: engine.synthesize_for_voice(
                    text=s,
                    voice=voice,
                    speaker_wav=ref_wav,
                    language=body.language,
                    speed=body.speed,
                ).wav_bytes,
                return_audio=True,
            )
        )

    pauses_ms = [hard_pause if boundary == "hard" else soft_pause for boundary in seg.boundary_types]
    _, sample_rate = wav_to_pcm16_mono(segment_results[0]["audio"], target_sample_rate=None)
    trimmed_wavs = []
    for result in segment_results:
        try:
            trimmed_wavs.append(trim_silence_pcm16(result["audio"], sample_rate))
        except Exception:
            trimmed_wavs.append(result["audio"])

    stitched_clean = stitch_wavs(
        trimmed_wavs,
        sample_rate=sample_rate,
        pauses_ms=pauses_ms,
        crossfade_ms=15,
    )

    processed = stitched_clean
    native_speed = engine.native_speed_supported_for_voice(voice)
    if abs(body.speed - 1.0) > 1e-3:
        if native_speed is False:
            try:
                processed = adjust_speed(processed, speed=body.speed)
            except Exception:
                processed = stitched_clean
        elif native_speed is None:
            # Unknown support: err on side of not double-applying; fallback only on failure.
            try:
                processed = adjust_speed(processed, speed=body.speed)
            except Exception:
                processed = stitched_clean

    if not body.radio_profile or body.radio_profile == "clean":
        final_wav = processed
    else:
        final_wav = apply_radio_profile(processed, profile=body.radio_profile, radio_intensity=radio_intensity)

    hits = sum(1 for r in segment_results if r.get("from_cache"))
    total = len(segment_results)
    if hits == total:
        cache_header = "HIT"
    elif hits == 0:
        cache_header = "MISS"
    else:
        cache_header = "MIXED"

    headers = {
        "X-Cache-Mode": "segment",
        "X-Cache": cache_header,
        "X-Cache-Segments": str(total),
        "X-Cache-Hits": str(hits),
        "X-Segment-Delimiter": seg.delimiter,
    }
    if norm_changed:
        headers["X-Normalized"] = "1"
    return Response(content=final_wav, media_type="audio/wav", headers=headers)


@app.post("/prefetch")
async def prefetch(body: PrefetchRequest):
    voice = voices.get(body.voice_id)
    if not voice:
        raise HTTPException(status_code=404, detail="Voice not found.")

    if body.radio_profile and not validate_radio_profile(body.radio_profile):
        raise HTTPException(status_code=400, detail="Unknown radio profile.")

    phrases = phrasepacks.get_phrases(body.phraseset, limit=body.limit)
    if not phrases:
        raise HTTPException(status_code=404, detail="Phrase set not found or empty.")

    voice_model_version = engine.model_version_for_voice(voice)
    items = []
    for phrase in phrases:
        normalized_atc, _ = normalize_atc(phrase)
        normalized = cache.normalize(normalized_atc)
        ref_wav = _resolve_ref_wav(voice)

        # Prefetch caches clean audio only (no stitching, no radio).
        result = cache.get_or_generate(
            model_version=voice_model_version,
            voice_id=body.voice_id,
            role=body.role,
            radio_profile=None,
            speed=body.speed,
            language=body.language,
            text=normalized,
            generator=lambda p=normalized: engine.synthesize_for_voice(
                text=p,
                voice=voice,
                speaker_wav=ref_wav,
                language=body.language,
                speed=body.speed,
            ).wav_bytes,
            return_audio=False,
        )
        items.append(
            {
                "text": normalized,
                "key": result["key"],
                "path": str(result["path"]),
                "from_cache": result["from_cache"],
            }
        )

    return {"voice_id": body.voice_id, "phraseset": body.phraseset, "count": len(items), "items": items}


@app.get("/cache/stats")
async def cache_stats():
    return cache.stats()


@app.get("/cache/recent")
async def cache_recent(limit: int = 50):
    limit = max(1, min(limit, 200))
    return cache.recent(limit)


@app.post("/cache/clear")
async def cache_clear():
    cache.clear()
    return {"status": "cleared"}


if __name__ == "__main__":
    import uvicorn

    uvicorn.run("xtts_service.app:app", host="127.0.0.1", port=8008, reload=True)
