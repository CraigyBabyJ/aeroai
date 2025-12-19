from __future__ import annotations

import datetime
import hashlib
import shutil
from pathlib import Path
from typing import Callable, Dict, Optional

from .db import CacheDB


def _hash_key(text: str) -> str:
    return hashlib.sha256(text.encode("utf-8")).hexdigest()


def _normalize_text(text: str) -> str:
    return " ".join(text.strip().split())


class TtsCache:
    def __init__(self, base_dir: Path, model_version: str = "unknown"):
        self.base_dir = Path(base_dir)
        self.cache_dir = self.base_dir / "cache_audio"
        self.data_dir = self.base_dir / "data"
        self.cache_dir.mkdir(parents=True, exist_ok=True)
        self.data_dir.mkdir(parents=True, exist_ok=True)
        self.db = CacheDB(self.data_dir / "cache.db")
        self.model_version = model_version

    def close(self) -> None:
        self.db.close()

    def __enter__(self):
        return self

    def __exit__(self, exc_type, exc, tb):
        self.close()
        return False

    def normalize(self, text: str) -> str:
        return _normalize_text(text)

    def make_key(
        self,
        model_version: str,
        voice_id: str,
        role: Optional[str],
        radio_profile: Optional[str],
        speed: float,
        language: str,
        text_norm: str,
    ) -> str:
        payload = "|".join(
            [
                model_version or self.model_version,
                voice_id or "",
                role or "",
                radio_profile or "",
                f"{speed:.3f}",
                language or "",
                text_norm,
            ]
        )
        return _hash_key(payload)

    def _sharded_path(self, voice_id: str, key: str) -> Path:
        shard1, shard2 = key[:2], key[2:4]
        return self.cache_dir / voice_id / shard1 / shard2 / f"{key}.wav"

    def get_cached(self, key: str) -> Optional[Dict]:
        record = self.db.fetch(key)
        if not record:
            return None
        path = Path(record["path"])
        if not path.exists():
            return None
        return {
            "key": key,
            "path": path,
            "bytes": record.get("bytes", 0),
            "record": record,
            "audio": path.read_bytes(),
        }

    def get_or_generate(
        self,
        *,
        model_version: str,
        voice_id: str,
        role: Optional[str],
        radio_profile: Optional[str],
        speed: float,
        language: str,
        text: str,
        generator: Callable[[], bytes],
        return_audio: bool = True,
    ) -> Dict:
        text_norm = self.normalize(text)
        key = self.make_key(model_version, voice_id, role, radio_profile, speed, language, text_norm)
        cached = self.get_cached(key)
        if cached:
            now = datetime.datetime.now(datetime.UTC).isoformat()
            self.db.record_hit(key, now)
            cached["record"]["hit_count"] = (cached["record"].get("hit_count") or 0) + 1
            cached["record"]["last_hit"] = now
            if not return_audio:
                cached.pop("audio", None)
            cached["from_cache"] = True
            return cached

        audio_bytes = generator()
        path = self._sharded_path(voice_id, key)
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_bytes(audio_bytes)
        created_at = datetime.datetime.now(datetime.UTC).isoformat()
        self.db.upsert(
            {
                "key": key,
                "voice_id": voice_id,
                "role": role,
                "radio_profile": radio_profile,
                "speed": speed,
                "language": language,
                "text_norm": text_norm,
                "path": str(path),
                "bytes": len(audio_bytes),
                "created_at": created_at,
                "last_hit": None,
                "hit_count": 0,
                "model_version": model_version or self.model_version,
            }
        )
        return {
            "key": key,
            "path": path,
            "bytes": len(audio_bytes),
            "from_cache": False,
            "audio": audio_bytes if return_audio else None,
        }

    def stats(self) -> Dict[str, int]:
        return self.db.stats()

    def recent(self, limit: int = 50):
        return self.db.recent(limit)

    def store_audio(
        self,
        *,
        model_version: str,
        voice_id: str,
        role: Optional[str],
        radio_profile: Optional[str],
        speed: float,
        language: str,
        text_norm: str,
        audio_bytes: bytes,
    ) -> Dict:
        """Write audio bytes to a sharded path and index them."""
        key = self.make_key(model_version, voice_id, role, radio_profile, speed, language, text_norm)
        path = self._sharded_path(voice_id, key)
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_bytes(audio_bytes)
        created_at = datetime.datetime.now(datetime.UTC).isoformat()
        self.db.upsert(
            {
                "key": key,
                "voice_id": voice_id,
                "role": role,
                "radio_profile": radio_profile,
                "speed": speed,
                "language": language,
                "text_norm": text_norm,
                "path": str(path),
                "bytes": len(audio_bytes),
                "created_at": created_at,
                "last_hit": None,
                "hit_count": 0,
                "model_version": model_version or self.model_version,
            }
        )
        return {"key": key, "path": path, "bytes": len(audio_bytes), "from_cache": False, "audio": audio_bytes}

    def clear(self) -> None:
        if self.cache_dir.exists():
            shutil.rmtree(self.cache_dir, ignore_errors=True)
        self.cache_dir.mkdir(parents=True, exist_ok=True)
        self.db.clear()
