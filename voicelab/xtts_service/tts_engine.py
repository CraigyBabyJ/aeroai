from __future__ import annotations

import io
import math
import tempfile
import wave
import os
import sys
from dataclasses import dataclass
from pathlib import Path
from typing import Any, Dict, Optional

from fastapi import HTTPException

from .espeak import ensure_espeak_backend


DEFAULT_MODEL_NAME = os.environ.get("XTTS_MODEL_NAME", "tts_models/multilingual/multi-dataset/xtts_v2")


@dataclass
class TtsResult:
    wav_bytes: bytes
    sample_rate: int


class TtsEngine:
    """
    XTTS wrapper with a sine-wave fallback so the service stays runnable.

    Hook points:
      - _load_xtts(): load and cache your real XTTS model.
      - _synthesize_xtts(): generate WAV bytes via the real model.
    """

    def __init__(self, base_dir: Path):
        self.base_dir = Path(base_dir)
        self.model_name = DEFAULT_MODEL_NAME
        self.model_version = "xtts-placeholder-0.2"
        self.model_loaded = False
        self.engine_error: Optional[str] = None
        self.xtts_available = False
        self.cuda_available = self._detect_cuda()
        self._xtts_model = None
        self._speed_param_supported: Optional[bool] = None
        self._try_load()

        # Secondary engine: Coqui VITS (VCTK)
        self.coqui_model_name = "tts_models/en/vctk/vits"
        self.coqui_model_version = self.coqui_model_name
        self.coqui_available = False
        self.coqui_loaded = False
        self.coqui_error: Optional[str] = None
        self._coqui_model = None
        self._coqui_speed_param_supported: Optional[bool] = None
        self.espeak_status: Dict[str, Any] = {}
        self._try_load_coqui()

    def _detect_cuda(self) -> bool:
        try:
            import torch  # type: ignore

            return bool(torch.cuda.is_available())
        except Exception:
            return False

    def _try_load(self) -> None:
        try:
            self.xtts_available = self._load_xtts()
            self.engine_error = None
        except ImportError as exc:
            self.engine_error = str(exc) or "XTTS libraries not installed."
            self.model_loaded = False
            self.xtts_available = False
        except Exception as exc:  # pragma: no cover - defensive
            self.engine_error = f"XTTS load failed: {exc}"
            self.model_loaded = False
            self.xtts_available = False

    def _try_load_coqui(self) -> None:
        self.espeak_status = ensure_espeak_backend(self.base_dir)
        espeak_missing = sys.platform == "win32" and not self.espeak_status.get("found")
        if espeak_missing:
            self.coqui_error = (
                "Coqui requires espeak-ng; install it or place espeak-ng.exe in xtts_service/tools/espeak-ng/"
            )
            self.coqui_available = False
            self.coqui_loaded = False
            self._coqui_model = None
            return
        try:
            from TTS.api import TTS  # type: ignore
        except ImportError as exc:
            self.coqui_error = str(exc) or "Coqui VITS libraries not installed."
            self.coqui_available = False
            self.coqui_loaded = False
            return

        device = "cuda" if self.cuda_available else "cpu"
        try:
            self._coqui_model = TTS(self.coqui_model_name).to(device)
            self.coqui_model_version = getattr(self._coqui_model, "version", self.coqui_model_version)
            self.coqui_available = True
            self.coqui_loaded = True
            self.coqui_error = None
        except Exception as exc:  # pragma: no cover - defensive
            self.coqui_error = f"Coqui VITS load failed: {exc}"
            self.coqui_available = True
            self.coqui_loaded = False
            self._coqui_model = None

    def _load_xtts(self) -> bool:
        """
        Try to initialize XTTS. Return True if libs are installed (even if load deferred).
        """
        try:
            # Some transformer versions relocated BeamSearchScorer; add a shim if needed.
            import transformers  # type: ignore

            if not hasattr(transformers, "BeamSearchScorer"):
                try:
                    from transformers.generation.beam_search import BeamSearchScorer as _BSS  # type: ignore

                    transformers.BeamSearchScorer = _BSS  # type: ignore[attr-defined]
                except Exception:
                    pass

            from TTS.api import TTS  # type: ignore
        except ImportError as exc:
            raise ImportError("XTTS libraries not installed. Install your XTTS stack to enable real synthesis.") from exc

        # Lazy-load the model now to avoid surprises later.
        device = "cuda" if self.cuda_available else "cpu"
        self._xtts_model = TTS(self.model_name).to(device)
        self.model_version = getattr(self._xtts_model, "version", self.model_version)
        self.model_loaded = True
        return True

    def _sine_fallback(self, text: str, speed: float) -> TtsResult:
        """Generate a short sine tone to keep the pipeline testable without XTTS."""
        duration = max(0.6, min(3.0, len(text) / 24.0))
        sr = 22050
        freq = 440.0
        frames = int(duration * sr / max(speed, 0.1))
        buffer = io.BytesIO()
        with wave.open(buffer, "wb") as wav:
            wav.setnchannels(1)
            wav.setsampwidth(2)
            wav.setframerate(sr)
            for i in range(frames):
                value = int(32767 * 0.2 * math.sin(2 * math.pi * freq * (i / sr)))
                wav.writeframesraw(value.to_bytes(2, "little", signed=True))
        return TtsResult(wav_bytes=buffer.getvalue(), sample_rate=sr)

    def _call_tts_to_file(self, *, model: Any, kwargs: Dict[str, Any], speed: float, speed_attr: str) -> None:
        """
        Call a Coqui model's tts_to_file while detecting whether it supports speed params.
        """
        try:
            model.tts_to_file(**{**kwargs, "speed": float(speed)})
            setattr(self, speed_attr, True)
            return
        except TypeError:
            pass
        try:
            model.tts_to_file(**{**kwargs, "speaking_rate": float(speed)})
            setattr(self, speed_attr, True)
            return
        except TypeError:
            pass

        setattr(self, speed_attr, False)
        model.tts_to_file(**kwargs)

    def _tts_to_file(self, *, text: str, speaker_wav: Path, language: str, speed: float, out_path: Path) -> None:
        if not self._xtts_model:
            raise RuntimeError("XTTS model not loaded.")
        kwargs = {
            "text": text.strip(),
            "file_path": str(out_path),
            "speaker_wav": str(speaker_wav),
            "language": language,
        }
        self._call_tts_to_file(
            model=self._xtts_model,
            kwargs=kwargs,
            speed=speed,
            speed_attr="_speed_param_supported",
        )

    def _apply_speed_fallback(self, wav_bytes: bytes, speed: float) -> bytes:
        """
        Fallback speed control by adjusting WAV header sample rate (affects pitch).
        Used when the underlying TTS API doesn't accept a speed parameter.
        """
        if abs(speed - 1.0) < 1e-6:
            return wav_bytes
        try:
            with wave.open(io.BytesIO(wav_bytes), "rb") as w:
                nch = w.getnchannels()
                sw = w.getsampwidth()
                sr = w.getframerate()
                nframes = w.getnframes()
                frames = w.readframes(nframes)
            new_sr = max(8000, min(int(sr * float(speed)), 96000))
            buf = io.BytesIO()
            with wave.open(buf, "wb") as out:
                out.setnchannels(nch)
                out.setsampwidth(sw)
                out.setframerate(new_sr)
                out.writeframes(frames)
            return buf.getvalue()
        except Exception:
            return wav_bytes

    def _synthesize_coqui(self, *, text: str, speaker_id: str, speed: float) -> Optional[TtsResult]:
        if not self._coqui_model:
            return None
        try:
            with tempfile.TemporaryDirectory() as td:
                out_path = Path(td) / "out.wav"
                kwargs = {
                    "text": text.strip(),
                    "file_path": str(out_path),
                    "speaker": speaker_id,
                }
                self._call_tts_to_file(
                    model=self._coqui_model,
                    kwargs=kwargs,
                    speed=speed,
                    speed_attr="_coqui_speed_param_supported",
                )
                wav_bytes = out_path.read_bytes()
                if self._coqui_speed_param_supported is False:
                    wav_bytes = self._apply_speed_fallback(wav_bytes, speed=speed)
                return TtsResult(wav_bytes=wav_bytes, sample_rate=0)
        except Exception as exc:
            self.coqui_error = f"Coqui VITS synth failed: {exc}"
            return None

    def _coqui_missing_espeak_message(self) -> str:
        return "Coqui requires espeak-ng; install it or place espeak-ng.exe in xtts_service/tools/espeak-ng/"

    def _resolve_engine(self, voice: Optional[Dict[str, Any]]) -> str:
        if not voice:
            return "xtts"
        engine = str(voice.get("engine") or "").strip().lower()
        return engine or "xtts"

    def _resolve_coqui_speaker_id(self, voice: Optional[Dict[str, Any]]) -> Optional[str]:
        if not voice:
            return None
        speaker_id = voice.get("speaker_id") or voice.get("speaker")
        if not speaker_id:
            return None
        return str(speaker_id)

    def model_version_for_voice(self, voice: Optional[Dict[str, Any]]) -> str:
        engine = self._resolve_engine(voice)
        if engine == "coqui_vits":
            speaker_id = self._resolve_coqui_speaker_id(voice) or "unknown"
            return f"coqui-vctk-vits:{speaker_id}"
        return self.model_version

    def native_speed_supported_for_voice(self, voice: Optional[Dict[str, Any]]) -> Optional[bool]:
        engine = self._resolve_engine(voice)
        if engine == "coqui_vits":
            return self._coqui_speed_param_supported
        return self._speed_param_supported

    def synthesize_for_voice(
        self,
        *,
        text: str,
        voice: Dict[str, Any],
        speed: float = 1.0,
        language: str = "en",
        speaker_wav: Optional[Path] = None,
    ) -> TtsResult:
        if not text or not text.strip():
            raise ValueError("Text is empty")

        engine = self._resolve_engine(voice)

        if engine == "coqui_vits":
            if not self._coqui_model:
                if not self.coqui_error and self.espeak_status.get("found") is False:
                    self.coqui_error = self._coqui_missing_espeak_message()
                raise HTTPException(
                    status_code=500,
                    detail=self.coqui_error or self._coqui_missing_espeak_message(),
                )

            speaker_id = self._resolve_coqui_speaker_id(voice)
            if not speaker_id:
                raise HTTPException(status_code=400, detail="Missing speaker_id for coqui_vits voice.")

            result = self._synthesize_coqui(text=text, speaker_id=speaker_id, speed=speed)
            if result:
                return result
            raise HTTPException(
                status_code=500,
                detail=self.coqui_error or self._coqui_missing_espeak_message(),
            )

        # Default: XTTS
        if not speaker_wav:
            raise ValueError("speaker_wav is required for xtts voices.")

        try:
            if self.xtts_available:
                if not self._xtts_model:
                    self._try_load()
                if self._xtts_model:
                    with tempfile.TemporaryDirectory() as td:
                        out_path = Path(td) / "out.wav"
                        self._tts_to_file(
                            text=text,
                            speaker_wav=speaker_wav,
                            language=language,
                            speed=speed,
                            out_path=out_path,
                        )
                        wav_bytes = out_path.read_bytes()
                        if self._speed_param_supported is False:
                            wav_bytes = self._apply_speed_fallback(wav_bytes, speed=speed)
                        return TtsResult(wav_bytes=wav_bytes, sample_rate=0)
        except Exception as exc:
            self.engine_error = f"XTTS synth failed: {exc}"

        return self._sine_fallback(text, speed=speed)

    def synthesize(
        self,
        *,
        text: str,
        voice: Optional[Dict[str, Any]] = None,
        speaker_wav: Optional[Path] = None,
        language: str = "en",
        speed: float = 1.0,
    ) -> TtsResult:
        voice_meta = voice or {"engine": "xtts"}
        try:
            return self.synthesize_for_voice(
                text=text,
                voice=voice_meta,
                speed=speed,
                language=language,
                speaker_wav=speaker_wav,
            )
        except Exception as exc:
            self.engine_error = f"Synthesis failed: {exc}"
            return self._sine_fallback(text, speed=speed)

    def health(self) -> dict:
        if self._xtts_model and self._coqui_model:
            active_mode = "multi"
        elif self._xtts_model:
            active_mode = "xtts_only"
        elif self._coqui_model:
            active_mode = "coqui_only"
        else:
            active_mode = "placeholder"
        return {
            "model_version": self.model_version,
            "model_loaded": self.model_loaded,
            "cuda_available": self.cuda_available,
            "engine_error": self.engine_error,
            "xtts_available": self.xtts_available,
            "xtts_ready": bool(self._xtts_model),
            "speed_supported": self._speed_param_supported,
            "mode": active_mode,
            "coqui_available": self.coqui_available,
            "coqui_loaded": self.coqui_loaded,
            "coqui_error": self.coqui_error,
            "coqui_ready": bool(self._coqui_model),
            "coqui_model_version": self.coqui_model_version,
            "coqui_model_name": self.coqui_model_name,
            "espeak": self.espeak_status,
            "espeak_found": bool(self.espeak_status.get("found")) if self.espeak_status else False,
            "espeak_source": self.espeak_status.get("source") if self.espeak_status else None,
            "espeak_exe": self.espeak_status.get("exe") if self.espeak_status else None,
        }
