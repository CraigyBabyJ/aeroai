from __future__ import annotations

import re
from typing import Dict, List, Optional, Tuple

_DIACRITIC_MAP = {
    ord("ä"): "ae",
    ord("ö"): "oe",
    ord("ü"): "ue",
    ord("Ä"): "Ae",
    ord("Ö"): "Oe",
    ord("Ü"): "Ue",
    ord("ß"): "ss",
    ord("á"): "a",
    ord("à"): "a",
    ord("â"): "a",
    ord("ã"): "a",
    ord("å"): "a",
    ord("Á"): "A",
    ord("À"): "A",
    ord("Â"): "A",
    ord("Ã"): "A",
    ord("Å"): "A",
    ord("é"): "e",
    ord("è"): "e",
    ord("ê"): "e",
    ord("ë"): "e",
    ord("É"): "E",
    ord("È"): "E",
    ord("Ê"): "E",
    ord("Ë"): "E",
    ord("í"): "i",
    ord("ì"): "i",
    ord("î"): "i",
    ord("ï"): "i",
    ord("Í"): "I",
    ord("Ì"): "I",
    ord("Î"): "I",
    ord("Ï"): "I",
    ord("ó"): "o",
    ord("ò"): "o",
    ord("ô"): "o",
    ord("õ"): "o",
    ord("Ó"): "O",
    ord("Ò"): "O",
    ord("Ô"): "O",
    ord("Õ"): "O",
    ord("ú"): "u",
    ord("ù"): "u",
    ord("û"): "u",
    ord("Ú"): "U",
    ord("Ù"): "U",
    ord("Û"): "U",
    ord("ñ"): "n",
    ord("Ñ"): "N",
    ord("ç"): "c",
    ord("Ç"): "C",
}


def normalize_diacritics(text: str) -> str:
    if not text:
        return text
    return text.translate(_DIACRITIC_MAP)


def _match_case(source: str, replacement: str) -> str:
    if source.isupper():
        return replacement.upper()
    if source.islower():
        return replacement.lower()
    if source.istitle():
        return replacement.title()
    return replacement


def apply_pronunciation_map(
    text: str,
    mapping: Dict[str, str],
    applied: Optional[List[Tuple[str, str]]] = None,
) -> str:
    if not text or not mapping:
        return text
    keys = [key.strip() for key in mapping.keys() if isinstance(key, str) and key.strip()]
    if not keys:
        return text
    keys.sort(key=len, reverse=True)
    lookup = {key.lower(): mapping[key] for key in keys if key in mapping}
    pattern = re.compile(r"\b(" + "|".join(re.escape(key) for key in keys) + r")\b", re.IGNORECASE)
    seen = set()

    def repl(match: re.Match) -> str:
        source = match.group(0)
        replacement = lookup.get(source.lower(), source)
        result = _match_case(source, replacement)
        if applied is not None:
            key_lower = source.lower()
            if key_lower not in seen:
                seen.add(key_lower)
                applied.append((source, result))
        return result

    return pattern.sub(repl, text)
