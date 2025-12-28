from __future__ import annotations

import re
from typing import Tuple


ACRONYMS = {"qnh", "qfe", "atis", "ils", "vor", "ndb", "rnav", "sid", "star", "ifr", "vfr", "ctaf", "unicom"}


DIGIT_WORDS = {
    "0": "zero",
    "1": "one",
    "2": "two",
    "3": "three",
    "4": "four",
    "5": "five",
    "6": "six",
    "7": "seven",
    "8": "eight",
    "9": "nine",
}


def _digits_to_words(digits: str) -> str:
    return " ".join(DIGIT_WORDS.get(d, d) for d in digits)


def _normalize_runway(text: str) -> str:
    def repl(match: re.Match) -> str:
        prefix = match.group(1).lower()
        num = match.group(2)
        side = match.group(3) or ""
        num_padded = num.zfill(2)
        spoken = _digits_to_words(num_padded)
        side_word = {"l": " left", "r": " right", "c": " center"}.get(side.lower(), "")
        return f"{prefix} {spoken}{side_word}"

    pattern = re.compile(r"\b(runway|rwy)\s*(\d{1,2})([lrcLRC]?)\b", re.IGNORECASE)
    return pattern.sub(repl, text)


def _normalize_flight_level(text: str) -> str:
    def repl(match: re.Match) -> str:
        digits = match.group(1)
        padded = digits.zfill(3)
        spoken = _digits_to_words(padded)
        return f"flight level {spoken}"

    pattern = re.compile(r"\b(?:FL|flight level)\s*(\d{2,3})\b", re.IGNORECASE)
    return pattern.sub(repl, text)


def _normalize_heading(text: str) -> str:
    def repl(match: re.Match) -> str:
        prefix = match.group(1).lower()
        digits = match.group(2).zfill(3)
        spoken = _digits_to_words(digits)
        return f"{prefix} {spoken}"

    pattern = re.compile(r"\b(heading|hdg|turn)\s*(\d{1,3})\b", re.IGNORECASE)
    return pattern.sub(repl, text)


def _normalize_squawk(text: str) -> str:
    def repl(match: re.Match) -> str:
        prefix = match.group(1).lower()
        digits = match.group(2).zfill(4)
        spoken = _digits_to_words(digits)
        return f"{prefix} {spoken}"

    pattern = re.compile(r"\b(squawk)\s*(\d{1,4})\b", re.IGNORECASE)
    return pattern.sub(repl, text)


def _normalize_qnh_qfe(text: str) -> str:
    def repl(match: re.Match) -> str:
        prefix = match.group(1).upper()
        digits = match.group(2).zfill(4)
        spoken = _digits_to_words(digits)
        prefix_letters = " ".join(prefix)
        return f"{prefix_letters} {spoken}"

    pattern = re.compile(r"\b(qnh|qfe)\s*(\d{3,4})\b", re.IGNORECASE)
    return pattern.sub(repl, text)


def _normalize_frequency(text: str) -> str:
    def repl(match: re.Match) -> str:
        whole = match.group(1)
        frac = match.group(2)
        whole_spoken = _digits_to_words(whole)
        frac_trimmed = frac.rstrip("0") or "0"
        frac_spoken = _digits_to_words(frac_trimmed)
        return f"{whole_spoken} decimal {frac_spoken}"

    pattern = re.compile(r"\b(\d{3})\.(\d{1,3})\b")
    return pattern.sub(repl, text)


def _normalize_callsign_numbers(text: str) -> str:
    """
    Convert flight numbers in callsigns to digit-by-digit format.
    Examples:
    - "Air Canada 223" -> "Air Canada two two three"
    - "ACA 223" -> "ACA two two three"
    - "British Airways 456" -> "British Airways four five six"
    """
    def repl_airline_name(match: re.Match) -> str:
        airline = match.group(1).strip()
        number = match.group(2)
        number_words = _digits_to_words(number)
        return f"{airline} {number_words}"

    def repl_icao(match: re.Match) -> str:
        icao = match.group(1)
        number = match.group(2)
        number_words = _digits_to_words(number)
        return f"{icao} {number_words}"

    # Pattern 1: Airline names (capitalized words) followed by digits
    # Matches: "Air Canada 223", "British Airways 456"
    pattern1 = re.compile(r"\b((?:[A-Z][a-z]+(?:\s+[A-Z][a-z]+)*))\s+(\d{1,4})\b")
    text = pattern1.sub(repl_airline_name, text)

    # Pattern 2: ICAO codes (2-3 uppercase letters) followed by digits
    # Matches: "ACA 223", "BAW 456"
    # This must come after airline name pattern to avoid conflicts
    pattern2 = re.compile(r"\b([A-Z]{2,3})\s+(\d{1,4})\b")
    text = pattern2.sub(repl_icao, text)

    return text


def _normalize_acronyms(text: str) -> str:
    def repl(match: re.Match) -> str:
        word = match.group(0).upper()
        return " ".join(word)

    pattern = re.compile(r"\b(" + "|".join(ACRONYMS) + r")\b", re.IGNORECASE)
    return pattern.sub(repl, text)


def _normalize_segment(segment: str) -> str:
    if not segment:
        return segment
    original = segment
    # Normalize whitespace inside the segment (not across delimiters).
    s = " ".join(segment.strip().split())
    s = _normalize_qnh_qfe(s)
    s = _normalize_flight_level(s)
    s = _normalize_runway(s)
    s = _normalize_heading(s)
    s = _normalize_squawk(s)
    s = _normalize_frequency(s)
    s = _normalize_callsign_numbers(s)  # Convert callsign numbers to digit-by-digit
    s = _normalize_acronyms(s)
    s = " ".join(s.strip().split())
    return s


def normalize_atc(text: str) -> Tuple[str, bool]:
    """
    Normalize ATC phrases while preserving pipe/newline delimiters.
    Returns (normalized_text, changed_flag).
    """
    if not text:
        return text, False

    delim_pattern = re.compile(r"(\||\r?\n)")
    tokens = delim_pattern.split(text)
    out_tokens = []
    changed = False

    for token in tokens:
        if token is None:
            continue
        if delim_pattern.fullmatch(token):
            out_tokens.append(token)
            continue
        normalized = _normalize_segment(token)
        if normalized != token:
            changed = True
        out_tokens.append(normalized)

    normalized_text = "".join(out_tokens)
    return normalized_text, changed
