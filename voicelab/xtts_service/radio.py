from __future__ import annotations

import io
import math
import random
import wave
from array import array
from typing import Dict, List


RADIO_PROFILES: Dict[str, Dict[str, str]] = {
    "clean": {"name": "Clean", "description": "Neutral, full-band voice."},
    "vhf": {"name": "VHF", "description": "Narrow-band VHF radio with hiss and light grit."},
    "cockpit": {"name": "Cockpit", "description": "Closed cockpit intercom flavor with low-mid focus."},
    "tinny": {"name": "Tinny", "description": "High-passed intercom flavor with light static."},
    # Added extra net-style flavors; remove these entries to return to original list.
    "vatsim": {"name": "VATSIM-ish", "description": "Narrow, hissy net audio with light dropouts and squelch."},
    "congested": {"name": "Congested Net", "description": "Heavier grit, packet-loss style dropouts, fast squelch tail."},
}

RADIO_DSP_VERSION = "2"


def list_radio_profiles() -> List[Dict[str, str]]:
    return [{"id": key, **value} for key, value in RADIO_PROFILES.items()]


def validate_radio_profile(name: str) -> bool:
    return name in RADIO_PROFILES


def _one_pole_lowpass(samples: array, alpha: float) -> array:
    out = array("h")
    y_prev = 0.0
    for x in samples:
        y_prev = y_prev + alpha * (x - y_prev)
        out.append(int(y_prev))
    return out


def _one_pole_highpass(samples: array, alpha: float) -> array:
    out = array("h")
    y_prev = 0.0
    x_prev = 0.0
    for x in samples:
        y_prev = alpha * (y_prev + x - x_prev)
        x_prev = x
        out.append(int(y_prev))
    return out


def _fade_tail(samples: array, sample_rate: int, tail_seconds: float = 0.08) -> array:
    count = int(sample_rate * tail_seconds)
    if count <= 0 or count > len(samples):
        return samples
    out = array("h", samples)
    start = len(samples) - count
    for i in range(count):
        factor = max(0.0, 1.0 - (i / count))
        out[start + i] = int(out[start + i] * factor)
    return out


def _add_noise(samples: array, level: float = 0.01) -> array:
    if level <= 0:
        return samples
    out = array("h")
    for x in samples:
        noise = random.uniform(-1.0, 1.0) * 32767 * level
        out.append(int(max(min(x + noise, 32767), -32768)))
    return out


def _add_static_bursts(samples: array, level: float, probability: float, burst_len: int) -> array:
    if level <= 0 or probability <= 0:
        return samples
    out = array("h", samples)
    i = 0
    length = len(out)
    while i < length:
        if random.random() < probability:
            blen = min(burst_len, length - i)
            for j in range(blen):
                noise = random.uniform(-1.0, 1.0) * 32767 * level
                idx = i + j
                out[idx] = int(max(min(out[idx] + noise, 32767), -32768))
            i += blen
        else:
            i += 1
    return out


def _apply_dropouts(samples: array, probability: float, max_len: int) -> array:
    if probability <= 0 or max_len <= 0:
        return samples
    out = array("h", samples)
    i = 0
    n = len(out)
    while i < n:
        if random.random() < probability:
            drop = random.randint(1, max_len)
            end = min(n, i + drop)
            for j in range(i, end):
                out[j] = 0
            i = end
        else:
            i += 1
    return out


def _am_wobble(samples: array, sample_rate: int, depth: float, rate_hz: float) -> array:
    if depth <= 0 or rate_hz <= 0 or not samples:
        return samples
    depth = max(0.0, min(depth, 1.0))
    out = array("h")
    two_pi_f = 2 * math.pi * rate_hz
    for idx, x in enumerate(samples):
        t = idx / float(sample_rate)
        mod = 1.0 - depth * (0.5 - 0.5 * math.cos(two_pi_f * t))
        y = int(x * mod)
        out.append(int(max(min(y, 32767), -32768)))
    return out


def _bitcrush(samples: array, drop_bits: int = 2) -> array:
    if drop_bits <= 0:
        return samples
    out = array("h")
    mask = ~((1 << drop_bits) - 1)
    for x in samples:
        out.append(int(x) & mask)
    return out


def _soft_clip(samples: array, drive: float = 1.0) -> array:
    out = array("h")
    for x in samples:
        y = math.tanh(drive * (x / 32767.0)) * 32767.0
        out.append(int(max(min(y, 32767), -32768)))
    return out


def _mix(dry: array, wet: array, wet_mix: float) -> array:
    wet_mix = max(0.0, min(1.0, wet_mix))
    dry_mix = 1.0 - wet_mix
    out = array("h")
    for a, b in zip(dry, wet):
        y = a * dry_mix + b * wet_mix
        out.append(int(max(min(y, 32767), -32768)))
    return out


def _normalize_peak(samples: array, target: float = 0.9) -> array:
    if not samples:
        return samples
    peak = max(abs(int(x)) for x in samples)
    if peak <= 0:
        return samples
    scale = (target * 32767.0) / peak
    out = array("h")
    for x in samples:
        y = int(x * scale)
        out.append(int(max(min(y, 32767), -32768)))
    return out


def apply_radio_profile(audio_wav: bytes, profile: str) -> bytes:
    """Apply a lightweight radio flavor. Falls back to passthrough on error."""
    if not profile or profile == "clean" or profile not in RADIO_PROFILES:
        return audio_wav
    try:
        with wave.open(io.BytesIO(audio_wav), "rb") as wav_in:
            params = wav_in.getparams()
            sr = params.framerate
            frames = wav_in.readframes(params.nframes)
    except Exception:
        return audio_wav

    if params.sampwidth != 2 or params.nchannels != 1:
        # Keep simple; only process mono 16-bit PCM
        return audio_wav

    samples = array("h", frames)

    # Profile parameters
    presets = {
        "vhf": {
            "hp": 320.0,
            "lp": 2800.0,
            "hiss": 0.004,
            "bursts_per_s": 6.0,
            "burst_level": 0.02,
            "burst_len_s": 0.012,
            "crush": 2,
            "drive": 1.25,
            "tail": 0.14,
            "wet": 0.85,
            "gain": 1.25,
        },
        "cockpit": {
            "hp": 200.0,
            "lp": 2400.0,
            "hiss": 0.0025,
            "bursts_per_s": 2.0,
            "burst_level": 0.012,
            "burst_len_s": 0.01,
            "crush": 1,
            "drive": 1.12,
            "tail": 0.1,
            "wet": 0.65,
            "gain": 1.15,
        },
        "tinny": {
            "hp": 520.0,
            "lp": 3200.0,
            "hiss": 0.0035,
            "bursts_per_s": 5.0,
            "burst_level": 0.018,
            "burst_len_s": 0.01,
            "crush": 2,
            "drive": 1.18,
            "tail": 0.12,
            "wet": 0.75,
            "gain": 1.2,
            "drop_prob": 0.0,
            "drop_len_s": 0.0,
            "wobble_hz": 0.0,
            "wobble_depth": 0.0,
        },
        "vatsim": {
            "hp": 320.0,
            "lp": 3000.0,
            "hiss": 0.0045,
            "bursts_per_s": 4.0,
            "burst_level": 0.022,
            "burst_len_s": 0.014,
            "crush": 1,
            "drive": 1.15,
            "tail": 0.1,
            "wet": 0.9,
            "gain": 1.12,
            "drop_prob": 0.0010,
            "drop_len_s": 0.018,
            "wobble_hz": 0.5,
            "wobble_depth": 0.1,
        },
        "congested": {
            "hp": 360.0,
            "lp": 2800.0,
            "hiss": 0.0055,
            "bursts_per_s": 7.0,
            "burst_level": 0.03,
            "burst_len_s": 0.014,
            "crush": 2,
            "drive": 1.25,
            "tail": 0.08,
            "wet": 0.95,
            "gain": 1.18,
            "drop_prob": 0.0025,
            "drop_len_s": 0.02,
            "wobble_hz": 0.65,
            "wobble_depth": 0.12,
        },
    }
    p = presets.get(profile, presets["vhf"])

    # Simple bandpass: high-pass then low-pass using single-pole filters.
    # Cutoffs tuned for radio-ish band (roughly 280-3200 Hz).
    dt = 1.0 / sr
    hp_rc = 1.0 / (2 * 3.14159 * p["hp"])
    lp_rc = 1.0 / (2 * 3.14159 * p["lp"])
    hp_alpha = hp_rc / (hp_rc + dt)
    lp_alpha = dt / (lp_rc + dt)

    band = _one_pole_highpass(samples, hp_alpha)
    band = _one_pole_lowpass(band, lp_alpha)

    wet = _bitcrush(band, drop_bits=p["crush"])
    wet = _soft_clip(wet, drive=p["drive"])
    wet = _add_noise(wet, level=p["hiss"])
    burst_prob = float(p["bursts_per_s"]) / float(sr)
    burst_len = max(1, int(float(sr) * float(p["burst_len_s"])))
    wet = _add_static_bursts(wet, level=p["burst_level"], probability=burst_prob, burst_len=burst_len)

    processed = _mix(band, wet, wet_mix=p["wet"])

    drop_prob = float(p.get("drop_prob", 0.0) or 0.0)
    drop_len_s = float(p.get("drop_len_s", 0.0) or 0.0)
    if drop_prob > 0 and drop_len_s > 0:
        max_drop = max(1, int(float(sr) * drop_len_s))
        processed = _apply_dropouts(processed, probability=drop_prob, max_len=max_drop)

    if p["gain"] != 1.0:
        gained = array("h")
        for x in processed:
            y = int(x * float(p["gain"]))
            gained.append(int(max(min(y, 32767), -32768)))
        processed = gained

    processed = _normalize_peak(processed, target=0.92)
    processed = _fade_tail(processed, sr, tail_seconds=p["tail"])

    wobble_hz = float(p.get("wobble_hz", 0.0) or 0.0)
    wobble_depth = float(p.get("wobble_depth", 0.0) or 0.0)
    if wobble_hz > 0 and wobble_depth > 0:
        processed = _am_wobble(processed, sample_rate=sr, depth=wobble_depth, rate_hz=wobble_hz)

    buf = io.BytesIO()
    try:
        with wave.open(buf, "wb") as wav_out:
            wav_out.setnchannels(1)
            wav_out.setsampwidth(2)
            wav_out.setframerate(sr)
            wav_out.writeframes(processed.tobytes())
        return buf.getvalue()
    except Exception:
        return audio_wav
