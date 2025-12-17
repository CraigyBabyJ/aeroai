import csv
import json
from pathlib import Path

INPUT_CSV = "airports.csv"  # change if needed
OUTPUT_JSON = "airports.json"  # final output

FIELDS = [
    "icao",
    "type",
    "name",
    "latitude_deg",
    "longitude_deg",
    "elevation_ft",
    "iso_country",
    "iso_region",
    "municipality",
]


def main():
    airports = {}

    with open(INPUT_CSV, newline="", encoding="utf-8") as f:
        reader = csv.DictReader(f)

        missing = [f for f in FIELDS if f not in reader.fieldnames]
        if missing:
            raise ValueError(f"CSV missing required columns: {missing}")

        for row in reader:
            icao = row["icao"].strip().upper()
            if len(icao) != 4:
                continue  # skip non-ICAO entries

            airports[icao] = {
                "type": row["type"].strip(),
                "name": row["name"].strip(),
                "municipality": row["municipality"].strip() or None,
                "latitude_deg": float(row["latitude_deg"]),
                "longitude_deg": float(row["longitude_deg"]),
                "elevation_ft": (
                    int(row["elevation_ft"]) if row["elevation_ft"] else None
                ),
                "iso_country": row["iso_country"].strip(),
                "iso_region": row["iso_region"].strip(),
            }

    with open(OUTPUT_JSON, "w", encoding="utf-8") as out:
        json.dump(airports, out, indent=2, ensure_ascii=False)

    print(f"✅ Wrote {len(airports)} airports to {OUTPUT_JSON}")


if __name__ == "__main__":
    main()
