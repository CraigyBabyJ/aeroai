from __future__ import annotations

import sqlite3
from pathlib import Path
from typing import Any, Dict, List, Optional


SCHEMA = """
CREATE TABLE IF NOT EXISTS tts_cache (
    key TEXT PRIMARY KEY,
    voice_id TEXT,
    role TEXT,
    radio_profile TEXT,
    speed REAL,
    language TEXT,
    text_norm TEXT,
    path TEXT,
    bytes INTEGER,
    created_at TEXT,
    last_hit TEXT,
    hit_count INTEGER,
    model_version TEXT
);
"""


class CacheDB:
    def __init__(self, db_path: Path):
        self.db_path = Path(db_path)
        self.db_path.parent.mkdir(parents=True, exist_ok=True)
        self.conn = sqlite3.connect(self.db_path, check_same_thread=False)
        self.conn.row_factory = sqlite3.Row
        self._ensure_schema()

    def _ensure_schema(self) -> None:
        with self.conn:
            self.conn.executescript(SCHEMA)
            self._migrate_columns()

    def _migrate_columns(self) -> None:
        """Ensure newly added columns exist when upgrading."""
        existing = {
            row["name"]
            for row in self.conn.execute("PRAGMA table_info(tts_cache)")
        }
        required_defs = {
            "last_hit": "TEXT",
            "hit_count": "INTEGER DEFAULT 0",
            "model_version": "TEXT",
            "language": "TEXT",
        }
        missing = [col for col in required_defs if col not in existing]
        for col in missing:
            self.conn.execute(f"ALTER TABLE tts_cache ADD COLUMN {col} {required_defs[col]}")

    def upsert(self, record: Dict[str, Any]) -> None:
        with self.conn:
            self.conn.execute(
                """
                INSERT OR REPLACE INTO tts_cache (
                    key, voice_id, role, radio_profile, speed, language, text_norm, path, bytes, created_at, last_hit, hit_count, model_version
                ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
                """,
                (
                    record["key"],
                    record["voice_id"],
                    record.get("role"),
                    record.get("radio_profile"),
                    record.get("speed"),
                    record.get("language"),
                    record.get("text_norm"),
                    record["path"],
                    record.get("bytes", 0),
                    record.get("created_at"),
                    record.get("last_hit"),
                    record.get("hit_count", 0),
                    record.get("model_version"),
                ),
            )

    def fetch(self, key: str) -> Optional[Dict[str, Any]]:
        cursor = self.conn.execute("SELECT * FROM tts_cache WHERE key = ?", (key,))
        row = cursor.fetchone()
        return dict(row) if row else None

    def record_hit(self, key: str, last_hit: str) -> None:
        with self.conn:
            self.conn.execute(
                """
                UPDATE tts_cache
                SET hit_count = COALESCE(hit_count, 0) + 1,
                    last_hit = ?
                WHERE key = ?
                """,
                (last_hit, key),
            )

    def stats(self) -> Dict[str, int]:
        cursor = self.conn.execute(
            "SELECT COUNT(*) AS items, COALESCE(SUM(bytes), 0) AS bytes FROM tts_cache"
        )
        row = cursor.fetchone()
        return {"items": int(row["items"]), "bytes": int(row["bytes"])}

    def recent(self, limit: int = 50) -> List[Dict[str, Any]]:
        cursor = self.conn.execute(
            """
            SELECT * FROM tts_cache
            ORDER BY datetime(COALESCE(last_hit, created_at)) DESC
            LIMIT ?
            """,
            (limit,),
        )
        rows = cursor.fetchall()
        return [dict(r) for r in rows]

    def clear(self) -> None:
        with self.conn:
            self.conn.execute("DELETE FROM tts_cache")

    def close(self) -> None:
        try:
            self.conn.close()
        except Exception:
            pass
