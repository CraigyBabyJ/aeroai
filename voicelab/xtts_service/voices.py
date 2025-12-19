from __future__ import annotations

import json
from pathlib import Path
from typing import Dict, List, Optional


class VoiceStore:
    def __init__(self, base_dir: Path):
        self.base_dir = Path(base_dir)
        self.voices_dir = self.base_dir / "voices"

    def list_voices(self) -> List[Dict]:
        voices: List[Dict] = []
        if not self.voices_dir.exists():
            return voices
        for meta_path in self.voices_dir.glob("*/meta.json"):
            voice_id = meta_path.parent.name
            try:
                data = json.loads(meta_path.read_text(encoding="utf-8"))
            except Exception:
                data = {}
            data.setdefault("id", voice_id)
            data.setdefault("name", voice_id.replace("_", " ").title())
            voices.append(data)
        return sorted(voices, key=lambda v: v.get("id", ""))

    def get(self, voice_id: str) -> Optional[Dict]:
        path = self.voices_dir / voice_id / "meta.json"
        if not path.exists():
            return None
        try:
            data = json.loads(path.read_text(encoding="utf-8"))
            data.setdefault("id", voice_id)
            return data
        except Exception:
            return None

    def get_ref_wav_path(self, voice_id: str) -> Optional[Path]:
        """Return the reference wav path if present."""
        ref_path = self.voices_dir / voice_id / "ref.wav"
        if ref_path.exists():
            return ref_path
        return None
