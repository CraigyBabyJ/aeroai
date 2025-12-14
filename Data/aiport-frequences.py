import csv
import io
import json
import urllib.request
from datetime import datetime, timezone
from pathlib import Path

# Script directory (works no matter where you run it from)
BASE_DIR = Path(__file__).resolve().parent

# Nightly-updated dataset
OURAIRPORTS_FREQ_CSV = "https://ourairports.com/data/airport-frequencies.csv"

# Map OurAirports "type" -> your JSON keys
TYPE_MAP = {
    "CLEARANCE": "clearance",
    "DELIVERY": "clearance",  # some entries use DELIVERY instead
    "GROUND": "ground",
    "TOWER": "tower",
    "DEPARTURE": "departure",
    "APPROACH": "approach",
}

FIELDS = ["clearance", "ground", "tower", "departure", "approach"]


def download_text(url: str) -> str:
    with urllib.request.urlopen(url, timeout=45) as r:
        return r.read().decode("utf-8", errors="replace")


def parse_ourairports(csv_text: str) -> dict[str, dict[str, float]]:
    """
    Returns: { ICAO: { 'tower': 118.7, 'ground': 121.9, ... } }
    If multiple freqs exist per type, we keep the first seen.
    """
    out: dict[str, dict[str, float]] = {}
    reader = csv.DictReader(io.StringIO(csv_text))

    for row in reader:
        ident = (row.get("airport_ident") or "").strip().upper()
        typ = (row.get("type") or "").strip().upper()
        mhz_str = (row.get("frequency_mhz") or "").strip()

        if not ident or not mhz_str:
            continue

        key = TYPE_MAP.get(typ)
        if not key:
            continue

        try:
            mhz = float(mhz_str)
        except ValueError:
            continue

        out.setdefault(ident, {})
        out[ident].setdefault(key, mhz)

    return out


def update_airport_frequencies_json(
    json_path: str | Path,
    *,
    only_fill_nulls: bool = True,  # True = fill nulls only (safe)
    add_meta: bool = False,  # optional meta info
    source_url: str = OURAIRPORTS_FREQ_CSV,
) -> None:
    json_path = Path(json_path)

    original_text = json_path.read_text(encoding="utf-8")
    data = json.loads(original_text)

    csv_text = download_text(source_url)
    latest = parse_ourairports(csv_text)

    now = datetime.now(timezone.utc).isoformat(timespec="seconds")

    changes = []
    for ident, rec in data.items():
        if not isinstance(rec, dict):
            continue
        src = latest.get(ident)
        if not src:
            continue

        for field in FIELDS:
            new_val = src.get(field)
            if new_val is None:
                continue

            cur_val = rec.get(field)

            if cur_val is None:
                rec[field] = new_val
                changes.append((ident, field, None, new_val))
            elif not only_fill_nulls and float(cur_val) != float(new_val):
                rec[field] = new_val
                changes.append((ident, field, cur_val, new_val))

        if add_meta:
            meta = rec.get("_meta") if isinstance(rec.get("_meta"), dict) else {}
            meta.update({"last_sync_utc": now, "source": source_url})
            rec["_meta"] = meta

    # Backup ORIGINAL file (same folder)
    stamp = datetime.now().strftime("%Y%m%d-%H%M%S")
    backup_path = json_path.with_name(f"{json_path.stem}.bak.{stamp}{json_path.suffix}")
    backup_path.write_text(original_text, encoding="utf-8")

    # Write updated file
    json_path.write_text(json.dumps(data, indent=2, sort_keys=True), encoding="utf-8")

    print(f"Updated: {json_path}")
    print(f"Backup:  {backup_path}")
    print(f"Changes: {len(changes)}")


if __name__ == "__main__":
    json_file = BASE_DIR / "airport-frequencies.json"

    update_airport_frequencies_json(
        json_file,
        only_fill_nulls=True,  # safest default
        add_meta=False,
    )

    # If you want to force-refresh (overwrite differences):
    # update_airport_frequencies_json(json_file, only_fill_nulls=False, add_meta=True)
