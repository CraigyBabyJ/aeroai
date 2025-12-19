from __future__ import annotations

import io
import math
import tempfile
import wave
import os
from dataclasses import dataclass
from pathlib import Path
from typing import Optional


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

    def _tts_to_file(self, *, text: str, speaker_wav: Path, language: str, speed: float, out_path: Path) -> None:
        if not self._xtts_model:
            raise RuntimeError("XTTS model not loaded.")
        kwargs = {
            "text": text.strip(),
            "file_path": str(out_path),
            "speaker_wav": str(speaker_wav),
            "language": language,
        }
        # Coqui's API varies; try speed-related params, then fall back.
        try:
            self._xtts_model.tts_to_file(**{**kwargs, "speed": float(speed)})
            self._speed_param_supported = True
            return
        except TypeError:
            pass
        try:
            self._xtts_model.tts_to_file(**{**kwargs, "speaking_rate": float(speed)})
            self._speed_param_supported = True
            return
        except TypeError:
            pass

        self._speed_param_supported = False
        self._xtts_model.tts_to_file(**kwargs)

    def _apply_speed_fallback(self, wav_bytes: bytes, speed: float) -> bytes:
        """
        Fallback speed control by adjusting WAV header sample rate (affects pitch).
        Used only if the underlying XTTS API doesn't accept a speed parameter.
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

    def synthesize(
        self,
        *,
        text: str,
        speaker_wav: Optional[Path] = None,
        language: str = "en",
        speed: float = 1.0,
    ) -> TtsResult:
        if not text or not text.strip():
            raise ValueError("Text is empty")

        try:
            if speaker_wav and self.xtts_available:
                if not self._xtts_model:
                    # In case load failed earlier but libs exist, try once more.
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

    def health(self) -> dict:
        return {
            "model_version": self.model_version,
            "model_loaded": self.model_loaded,
            "cuda_available": self.cuda_available,
            "engine_error": self.engine_error,
            "xtts_available": self.xtts_available,
            "xtts_ready": bool(self._xtts_model),
            "speed_supported": self._speed_param_supported,
            "mode": "xtts" if self._xtts_model else "placeholder",
        }
