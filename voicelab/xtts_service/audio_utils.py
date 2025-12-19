from __future__ import annotations

import io
import wave
from array import array
from typing import List, Optional, Sequence, Tuple, Union


def wav_to_pcm16_mono(
    wav_bytes: bytes,
    *,
    target_sample_rate: Optional[int] = None,
) -> Tuple[bytes, int]:
    """
    Convert a WAV byte-string to PCM16 mono frames at a consistent sample rate.

    Uses stdlib only (wave). Raises ValueError for unsupported input.
    """
    try:
        with wave.open(io.BytesIO(wav_bytes), "rb") as w:
            nch = int(w.getnchannels())
            sw = int(w.getsampwidth())
            sr = int(w.getframerate())
            frames = w.readframes(w.getnframes())
    except Exception as exc:
        raise ValueError(f"Invalid WAV input: {exc}") from exc

    if nch not in (1, 2):
        raise ValueError(f"Only mono/stereo WAV is supported (got {nch} ch)")

    samples = _bytes_to_int16(frames, sampwidth=sw)

    if nch == 2:
        samples = _stereo_to_mono(samples)

    if target_sample_rate and sr != int(target_sample_rate):
        samples = _resample_linear(samples, src_rate=sr, dst_rate=int(target_sample_rate))
        sr = int(target_sample_rate)

    return samples.tobytes(), sr


def _bytes_to_int16(frames: bytes, *, sampwidth: int) -> array:
    if sampwidth == 2:
        return array("h", frames)
    if sampwidth == 1:
        out = array("h")
        for b in frames:
            out.append((int(b) - 128) << 8)
        return out
    if sampwidth == 3:
        out = array("h")
        for i in range(0, len(frames), 3):
            raw = int.from_bytes(frames[i : i + 3], "little", signed=True)
            out.append(int(max(min(raw >> 8, 32767), -32768)))
        return out
    if sampwidth == 4:
        out = array("h")
        for i in range(0, len(frames), 4):
            raw = int.from_bytes(frames[i : i + 4], "little", signed=True)
            out.append(int(max(min(raw >> 16, 32767), -32768)))
        return out
    raise ValueError(f"Unsupported WAV sample width: {sampwidth} bytes")


def _stereo_to_mono(stereo_samples: array) -> array:
    out = array("h")
    # stereo interleaved L,R
    for i in range(0, len(stereo_samples) - 1, 2):
        out.append(int((int(stereo_samples[i]) + int(stereo_samples[i + 1])) / 2))
    return out


def _resample_linear(samples: array, *, src_rate: int, dst_rate: int) -> array:
    if src_rate <= 0 or dst_rate <= 0:
        raise ValueError("Invalid sample rate")
    if src_rate == dst_rate or not samples:
        return array("h", samples)

    out_len = max(1, int(round(len(samples) * (dst_rate / src_rate))))
    out = array("h")
    scale = (len(samples) - 1) / max(1, out_len - 1)
    for i in range(out_len):
        pos = i * scale
        idx = int(pos)
        frac = pos - idx
        a = samples[idx]
        b = samples[min(idx + 1, len(samples) - 1)]
        out.append(int(a + (b - a) * frac))
    return out


def stitch_wavs(
    wav_chunks: Sequence[bytes],
    *,
    pause_ms: Union[int, Sequence[int]] = 140,
) -> bytes:
    """
    Stitch multiple WAV chunks into one WAV:
      - Convert all chunks to PCM16 mono
      - Resample to first chunk's sample rate
      - Insert silence between segments
      - Re-encode to a single WAV
    """
    if not wav_chunks:
        raise ValueError("No WAV chunks provided")

    pauses: List[int]
    if isinstance(pause_ms, int):
        pauses = [int(pause_ms)] * max(0, len(wav_chunks) - 1)
    else:
        pauses = [int(p) for p in pause_ms]
        if len(pauses) != max(0, len(wav_chunks) - 1):
            raise ValueError("pause_ms list must be len(chunks) - 1")

    first_pcm, sample_rate = wav_to_pcm16_mono(wav_chunks[0], target_sample_rate=None)
    pcm_parts: List[bytes] = [first_pcm]

    for idx, wav_bytes in enumerate(wav_chunks[1:], start=1):
        pause = max(0, int(pauses[idx - 1]))
        if pause:
            silence_frames = int(sample_rate * (pause / 1000.0))
            pcm_parts.append(b"\x00\x00" * silence_frames)
        pcm, _ = wav_to_pcm16_mono(wav_bytes, target_sample_rate=sample_rate)
        pcm_parts.append(pcm)

    stitched_pcm = b"".join(pcm_parts)

    buf = io.BytesIO()
    with wave.open(buf, "wb") as out:
        out.setnchannels(1)
        out.setsampwidth(2)
        out.setframerate(sample_rate)
        out.writeframes(stitched_pcm)
    return buf.getvalue()


def adjust_speed(
    wav_bytes: bytes,
    *,
    speed: float,
    min_speed: float = 0.5,
    max_speed: float = 1.5,
) -> bytes:
    """
    Resample PCM to change playback speed (pitch will shift).
    Keeps output sample rate constant while scaling frame count by 1/speed.
    """
    speed = float(speed)
    if speed <= 0:
        raise ValueError("Speed must be positive")
    speed = max(min_speed, min(max_speed, speed))
    if abs(speed - 1.0) < 1e-3:
        return wav_bytes

    pcm, sr = wav_to_pcm16_mono(wav_bytes, target_sample_rate=None)
    target_rate = int(sr / speed)
    target_rate = max(8000, min(target_rate, 96000))
    resampled = _resample_linear(array("h", pcm), src_rate=sr, dst_rate=target_rate)

    buf = io.BytesIO()
    with wave.open(buf, "wb") as out:
        out.setnchannels(1)
        out.setsampwidth(2)
        out.setframerate(sr)
        out.writeframes(resampled.tobytes())
    return buf.getvalue()
