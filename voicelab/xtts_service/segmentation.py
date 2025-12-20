from __future__ import annotations

import re
from dataclasses import dataclass
from typing import List, Literal, Tuple


DelimiterMode = Literal["pipe", "newline", "punct", "none"]


BoundaryType = Literal["hard", "soft"]


@dataclass(frozen=True)
class SegmentationResult:
    segments: List[str]
    delimiter: DelimiterMode
    pauses_ms: List[int]
    boundary_types: List[BoundaryType]


_PUNCT_BREAKS = {".", "?", "!", ";"}


def _normalize_segment_text(text: str) -> str:
    return " ".join(text.strip().split())


def _split_punct(text: str) -> List[str]:
    segments: List[str] = []
    buf: List[str] = []
    for ch in text:
        buf.append(ch)
        if ch in _PUNCT_BREAKS:
            seg = _normalize_segment_text("".join(buf))
            if seg:
                segments.append(seg)
            buf = []
    tail = _normalize_segment_text("".join(buf))
    if tail:
        segments.append(tail)
    return segments


def _split_long_segment(
    segment: str,
    *,
    max_len: int = 220,
    min_len: int = 160,
) -> List[str]:
    segment = _normalize_segment_text(segment)
    if not segment:
        return []
    if len(segment) <= max_len:
        return [segment]

    words = segment.split()
    chunks: List[str] = []
    current = ""

    for word in words:
        if not current:
            if len(word) <= max_len:
                current = word
                continue
            # Single token longer than the max; hard-split to avoid losing text.
            chunks.extend([word[i : i + max_len] for i in range(0, len(word), max_len)])
            current = ""
            continue

        candidate = f"{current} {word}"
        if len(candidate) <= max_len:
            current = candidate
            continue

        chunks.append(current)
        current = word

    if current:
        chunks.append(current)

    # Try to avoid extremely short trailing chunks if they can merge safely.
    if len(chunks) >= 2 and len(chunks[-1]) < min_len:
        merged = f"{chunks[-2]} {chunks[-1]}"
        if len(merged) <= max_len:
            chunks[-2] = merged
            chunks.pop()

    return [c for c in (_normalize_segment_text(c) for c in chunks) if c]


def _clamp_pause(val: Optional[int], default: int) -> int:
    if val is None:
        return default
    try:
        return max(0, min(int(val), 1000))
    except Exception:
        return default


def segment_text(
    text: str,
    *,
    base_pause_ms: Optional[int] = None,
    wrap_pause_ms: Optional[int] = None,
) -> SegmentationResult:
    """
    Delimiter-first segmentation for ATC-style cadence.

    Priority:
      1) '|' (hard breaks)
      2) newlines (hard breaks)
      3) fallback punctuation breaks on . ? ! ;

    If any segment exceeds 220 chars, it is further split at word boundaries
    into ~160-220 char chunks.
    """
    raw = text or ""
    if "|" in raw:
        delimiter: DelimiterMode = "pipe"
        parts = [_normalize_segment_text(p) for p in raw.split("|")]
        base = [p for p in parts if p]
    elif "\n" in raw or "\r" in raw:
        delimiter = "newline"
        parts = re.split(r"\r?\n+", raw)
        base = [_normalize_segment_text(p) for p in parts if _normalize_segment_text(p)]
    else:
        delimiter = "punct" if any(ch in raw for ch in _PUNCT_BREAKS) else "none"
        base = _split_punct(raw)

    hard_pause_default = 70
    soft_pause_default = 35
    base_pause_default = hard_pause_default if delimiter in ("pipe", "newline") else soft_pause_default
    wrap_pause_default = soft_pause_default
    base_pause = _clamp_pause(base_pause_ms, base_pause_default)
    wrap_pause = _clamp_pause(wrap_pause_ms, wrap_pause_default)

    segments: List[str] = []
    pauses_ms: List[int] = []
    boundary_types: List[BoundaryType] = []
    for base_idx, base_seg in enumerate(base):
        subs = _split_long_segment(base_seg, max_len=220, min_len=160)
        for sub_idx, sub in enumerate(subs):
            if segments:
                if sub_idx > 0:
                    boundary = "soft"
                else:
                    boundary = "hard" if delimiter in ("pipe", "newline") else "soft"
                boundary_types.append(boundary)
                pauses_ms.append(base_pause if boundary == "hard" else wrap_pause)
            segments.append(sub)

    return SegmentationResult(
        segments=segments,
        delimiter=delimiter,
        pauses_ms=pauses_ms,
        boundary_types=boundary_types,
    )


def split_segments(text: str) -> list[str]:
    return segment_text(text).segments
