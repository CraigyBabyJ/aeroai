from __future__ import annotations

import json
from pathlib import Path
from typing import Dict, List, Optional


class VoiceStore:
    def __init__(self, base_dir: Path):
        self.base_dir = Path(base_dir)
        self.voices_dir = self.base_dir / "voices"

    def _load_meta(self, voice_id: str, meta_path: Path) -> Dict:
        data: Dict = {}
        try:
            maybe_data = json.loads(meta_path.read_text(encoding="utf-8"))
            if isinstance(maybe_data, dict):
                data = maybe_data
        except Exception:
            data = {}
        data.setdefault("id", voice_id)
        data.setdefault("name", voice_id.replace("_", " ").title())
        data.setdefault("engine", "xtts")
        return data

    def list_voices(self) -> List[Dict]:
        voices: List[Dict] = []
        if not self.voices_dir.exists():
            return voices
        for meta_path in self.voices_dir.glob("*/meta.json"):
            voice_id = meta_path.parent.name
            data = self._load_meta(voice_id, meta_path)
            voices.append(data)
        return sorted(voices, key=lambda v: v.get("id", ""))

    def get(self, voice_id: str) -> Optional[Dict]:
        path = self.voices_dir / voice_id / "meta.json"
        if not path.exists():
            return None
        try:
            return self._load_meta(voice_id, path)
        except Exception:
            return None

    def get_ref_wav_path(self, voice_id: str) -> Optional[Path]:
        """Return the reference wav path if present."""
        voice_dir = self.voices_dir / voice_id
        reference_path = voice_dir / "reference.wav"
        if reference_path.exists():
            return reference_path
        ref_path = voice_dir / "ref.wav"
        if ref_path.exists():
            return ref_path
        return None
