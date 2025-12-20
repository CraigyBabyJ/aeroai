from __future__ import annotations

import io
import math
import wave
from array import array
from typing import Iterable, Sequence

from .audio_utils import wav_to_pcm16_mono


def _pcm_to_wav_bytes(pcm: array, sample_rate: int) -> bytes:
    buf = io.BytesIO()
    with wave.open(buf, "wb") as out:
        out.setnchannels(1)
        out.setsampwidth(2)
        out.setframerate(int(sample_rate))
        out.writeframes(pcm.tobytes())
    return buf.getvalue()


def trim_silence_pcm16(
    wav_bytes: bytes,
    sample_rate: int,
    *,
    threshold_db: float = -45.0,
    pad_ms: int = 5,
) -> bytes:
    pcm_bytes, sr = wav_to_pcm16_mono(wav_bytes, target_sample_rate=sample_rate)
    samples = array("h", pcm_bytes)
    if not samples:
        return wav_bytes

    threshold = int(32767 * math.pow(10.0, float(threshold_db) / 20.0))
    if threshold < 1:
        threshold = 1

    start_idx = None
    end_idx = None
    for i, sample in enumerate(samples):
        if abs(sample) > threshold:
            start_idx = i
            break
    for i in range(len(samples) - 1, -1, -1):
        if abs(samples[i]) > threshold:
            end_idx = i
            break

    if start_idx is None or end_idx is None:
        return wav_bytes

    pad_frames = max(0, int(int(sr) * (int(pad_ms) / 1000.0)))
    start_idx = max(0, start_idx - pad_frames)
    end_idx = min(len(samples) - 1, end_idx + pad_frames)
    if end_idx <= start_idx:
        return wav_bytes

    trimmed = samples[start_idx : end_idx + 1]
    return _pcm_to_wav_bytes(trimmed, sr)


def make_silence_wav(sample_rate: int, ms: int) -> bytes:
    frames = max(0, int(int(sample_rate) * (int(ms) / 1000.0)))
    silence = array("h", [0] * frames)
    return _pcm_to_wav_bytes(silence, sample_rate)


def _apply_fade_out(samples: array, frames: int) -> None:
    if frames <= 0:
        return
    frames = min(frames, len(samples))
    for idx in range(frames):
        scale = (frames - idx) / float(frames)
        samples[-frames + idx] = int(samples[-frames + idx] * scale)


def _apply_fade_in(samples: array, frames: int) -> None:
    if frames <= 0:
        return
    frames = min(frames, len(samples))
    for idx in range(frames):
        scale = (idx + 1) / float(frames)
        samples[idx] = int(samples[idx] * scale)


def stitch_wavs(
    wavs: Sequence[bytes],
    *,
    sample_rate: int,
    pauses_ms: Sequence[int],
    crossfade_ms: int = 15,
) -> bytes:
    if not wavs:
        raise ValueError("No WAV chunks provided")
    if len(pauses_ms) != max(0, len(wavs) - 1):
        raise ValueError("pauses_ms list must be len(wavs) - 1")

    pcm_chunks: list[array] = []
    for wav_bytes in wavs:
        pcm_bytes, _ = wav_to_pcm16_mono(wav_bytes, target_sample_rate=sample_rate)
        pcm_chunks.append(array("h", pcm_bytes))

    crossfade_frames = max(0, int(int(sample_rate) * (int(crossfade_ms) / 1000.0)))

    stitched = array("h", pcm_chunks[0])
    for idx in range(1, len(pcm_chunks)):
        next_pcm = pcm_chunks[idx]
        pause_frames = max(0, int(int(sample_rate) * (int(pauses_ms[idx - 1]) / 1000.0)))

        if pause_frames > 0:
            _apply_fade_out(stitched, crossfade_frames)
            stitched.extend([0] * pause_frames)
            fade_pcm = array("h", next_pcm)
            _apply_fade_in(fade_pcm, crossfade_frames)
            stitched.extend(fade_pcm)
            continue

        overlap = min(crossfade_frames, len(stitched), len(next_pcm))
        if overlap > 0:
            mixed = array("h")
            for i in range(overlap):
                t = (i + 1) / float(overlap)
                mixed.append(int(stitched[-overlap + i] * (1.0 - t) + next_pcm[i] * t))
            stitched = stitched[: len(stitched) - overlap]
            stitched.extend(mixed)
            stitched.extend(next_pcm[overlap:])
        else:
            stitched.extend(next_pcm)

    return _pcm_to_wav_bytes(stitched, sample_rate)
