#!/usr/bin/env python3
"""
Convert airport-frequencies.csv to optimized JSON format.

The JSON output will be structured as:
{
  "ICAO": {
    "clearance": frequency or null,
    "ground": frequency or null,
    "tower": frequency or null,
    "departure": frequency or null,
    "approach": frequency or null
  }
}
"""

import csv
import json
import os
from collections import defaultdict

SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
CSV_FILE = os.path.join(SCRIPT_DIR, "airport-frequencies.csv")
JSON_FILE = os.path.join(SCRIPT_DIR, "airport-frequencies.json")

def contains_keywords(value, keywords):
    """Check if value contains any of the keywords (case-insensitive)."""
    if not value:
        return False
    value_lower = value.lower()
    return any(kw.lower() in value_lower for kw in keywords)

def determine_frequency_type(type_str, desc_str):
    """Determine which frequency type this entry represents."""
    combined = f"{type_str} {desc_str}".strip()
    
    if contains_keywords(combined, ["gnd", "ground"]):
        return "ground"
    elif contains_keywords(combined, ["clr", "clearance", "del", "delivery"]):
        return "clearance"
    elif contains_keywords(combined, ["twr", "tower"]):
        return "tower"
    elif contains_keywords(combined, ["dep", "departure"]):
        return "departure"
    elif contains_keywords(combined, ["app", "apch", "approach"]):
        return "approach"
    
    return None

def convert_csv_to_json():
    """Convert CSV to optimized JSON format."""
    frequencies_by_icao = defaultdict(dict)
    
    print(f"Reading {CSV_FILE}...")
    with open(CSV_FILE, 'r', encoding='utf-8') as f:
        reader = csv.DictReader(f)
        
        for row in reader:
            icao = row['airport_ident'].strip().strip('"')
            type_str = row['type'].strip().strip('"') if row['type'] else ""
            desc_str = row['description'].strip().strip('"') if row['description'] else ""
            freq_str = row['frequency_mhz'].strip().strip('"')
            
            if not icao or not freq_str:
                continue
            
            try:
                frequency = float(freq_str)
            except ValueError:
                continue
            
            freq_type = determine_frequency_type(type_str, desc_str)
            
            if freq_type:
                # Only set if not already set (keep first match, like original code)
                if freq_type not in frequencies_by_icao[icao]:
                    frequencies_by_icao[icao][freq_type] = frequency
    
    # Convert to final format with nulls for missing frequencies
    output = {}
    for icao, freqs in frequencies_by_icao.items():
        output[icao] = {
            "clearance": freqs.get("clearance"),
            "ground": freqs.get("ground"),
            "tower": freqs.get("tower"),
            "departure": freqs.get("departure"),
            "approach": freqs.get("approach")
        }
    
    print(f"Writing {JSON_FILE}...")
    with open(JSON_FILE, 'w', encoding='utf-8') as f:
        json.dump(output, f, indent=2, ensure_ascii=False)
    
    print(f"✓ Converted {len(output)} airports to JSON")
    print(f"✓ File size: {os.path.getsize(JSON_FILE) / 1024 / 1024:.2f} MB")

if __name__ == "__main__":
    convert_csv_to_json()

