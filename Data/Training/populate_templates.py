"""
AeroAI Template Populator
Fills in placeholder templates with realistic random values.
"""

import json
import os
import re
import random

SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
INPUT_FILE = os.path.join(SCRIPT_DIR, "training_aeroai.jsonl")
OUTPUT_FILE = os.path.join(SCRIPT_DIR, "training_aeroai_populated.jsonl")

# ============================================================================
# REALISTIC DATA POOLS
# ============================================================================

RUNWAYS = [
    "09",
    "09L",
    "09R",
    "27",
    "27L",
    "27R",
    "22",
    "22L",
    "22R",
    "04",
    "04L",
    "04R",
    "26",
    "26L",
    "26R",
    "32",
    "14",
    "18",
    "36",
    "06",
    "24",
    "28",
    "10",
]

RUNWAY_SPOKEN = {
    "09": "zero niner",
    "09L": "zero niner left",
    "09R": "zero niner right",
    "27": "two seven",
    "27L": "two seven left",
    "27R": "two seven right",
    "22": "two two",
    "22L": "two two left",
    "22R": "two two right",
    "04": "zero four",
    "04L": "zero four left",
    "04R": "zero four right",
    "26": "two six",
    "26L": "two six left",
    "26R": "two six right",
    "32": "three two",
    "14": "one four",
    "18": "one eight",
    "36": "three six",
    "06": "zero six",
    "24": "two four",
    "28": "two eight",
    "10": "one zero",
}

HEADINGS = [
    "010",
    "030",
    "060",
    "090",
    "120",
    "150",
    "180",
    "210",
    "240",
    "270",
    "300",
    "330",
    "360",
]

ALTITUDES = ["2000", "3000", "4000", "5000", "6000", "7000", "8000"]

SPEEDS = ["160", "180", "200", "210", "220", "250"]

WIND_DIRS = [
    "240",
    "250",
    "260",
    "270",
    "280",
    "290",
    "300",
    "310",
    "320",
    "180",
    "190",
    "090",
    "100",
]

WIND_SPEEDS = ["6", "8", "10", "12", "14", "15", "18"]

QNH_VALUES = ["1008", "1010", "1012", "1013", "1015", "1016", "1018", "1020", "1022"]

APPROACH_TYPES = ["ILS", "RNAV", "visual"]

TAXI_ROUTES = [
    "alpha, bravo",
    "alpha, charlie",
    "bravo, delta",
    "november, sierra",
    "mike, november",
    "alpha, tango",
    "sierra, quebec",
    "lima, mike",
    "delta, echo",
    "foxtrot, golf",
    "kilo, lima",
    "papa, quebec",
]

EXIT_TAXIWAYS = [
    "alpha one",
    "bravo two",
    "charlie three",
    "delta",
    "echo five",
    "foxtrot",
    "golf two",
    "hotel",
    "november one",
    "sierra three",
]

EMERGENCY_TYPES = [
    "engine fire",
    "engine failure",
    "medical emergency",
    "hydraulic failure",
    "smoke in cockpit",
    "fuel emergency",
    "pressurisation failure",
    "bird strike",
]


def speak_number(num_str):
    """Convert number to spoken form."""
    mapping = {
        "0": "zero",
        "1": "one",
        "2": "two",
        "3": "three",
        "4": "four",
        "5": "five",
        "6": "six",
        "7": "seven",
        "8": "eight",
        "9": "niner",
    }
    return " ".join(mapping.get(c, c) for c in str(num_str))


def get_runway_spoken(rwy):
    """Get spoken runway or generate it."""
    if rwy in RUNWAY_SPOKEN:
        return RUNWAY_SPOKEN[rwy]
    # Generate spoken form
    result = []
    for c in rwy:
        if c.isdigit():
            result.append(speak_number(c))
        elif c == "L":
            result.append("left")
        elif c == "R":
            result.append("right")
        elif c == "C":
            result.append("centre")
    return " ".join(result)


def populate_placeholders(text):
    """Replace placeholders with realistic values."""

    # Track consistent values within same message
    rwy = random.choice(RUNWAYS)
    rwy_spoken = get_runway_spoken(rwy)
    bad_rwy = random.choice([r for r in RUNWAYS if r != rwy])
    bad_rwy_spoken = get_runway_spoken(bad_rwy)

    heading = random.choice(HEADINGS)
    heading_spoken = speak_number(heading)

    altitude = random.choice(ALTITUDES)
    altitude_spoken = speak_number(altitude[0]) + " thousand"

    speed = random.choice(SPEEDS)
    speed_spoken = speak_number(speed)

    wind_dir = random.choice(WIND_DIRS)
    wind_dir_spoken = speak_number(wind_dir)

    wind_spd = random.choice(WIND_SPEEDS)
    wind_spd_spoken = speak_number(wind_spd)

    qnh = random.choice(QNH_VALUES)

    approach = random.choice(APPROACH_TYPES)

    taxi_route = random.choice(TAXI_ROUTES)

    exit_twy = random.choice(EXIT_TAXIWAYS)

    emergency = random.choice(EMERGENCY_TYPES)

    # Replace placeholders
    replacements = {
        "{ARR_RWY}": rwy,
        "{ARR_RWY_SPOKEN}": rwy_spoken,
        "{ARR_RWY_BAD_SPOKEN}": bad_rwy_spoken,
        "{DEP_RWY}": rwy,
        "{DEP_RWY_SPOKEN}": rwy_spoken,
        "{DEP_RWY_BAD_SPOKEN}": bad_rwy_spoken,
        "{HEADING}": heading,
        "{HEADING_SPOKEN}": heading_spoken,
        "{ALTITUDE_FT}": altitude,
        "{ALTITUDE_SPOKEN}": altitude_spoken,
        "{SPEED}": speed,
        "{SPEED_SPOKEN}": speed_spoken,
        "{WIND_DIR}": wind_dir,
        "{WIND_DIR_SPOKEN}": wind_dir_spoken,
        "{WIND_SPD}": wind_spd,
        "{WIND_SPD_SPOKEN}": wind_spd_spoken,
        "{QNH_HPA}": qnh,
        "{QNH}": qnh,
        "{APPROACH_TYPE}": approach,
        "{TAXI_ROUTE}": taxi_route,
        "{EXIT}": exit_twy,
        "{EMERGENCY_NATURE}": emergency,
    }

    for placeholder, value in replacements.items():
        text = text.replace(placeholder, value)

    return text


def has_placeholders(text):
    """Check if text contains unfilled placeholders."""
    return bool(re.search(r"\{[A-Z_]+\}", text))


def process_example(obj):
    """Process a single example, populating any placeholders."""
    messages = obj.get("messages", [])
    new_messages = []

    for msg in messages:
        new_msg = msg.copy()
        content = msg.get("content", "")

        if has_placeholders(content):
            content = populate_placeholders(content)

        new_msg["content"] = content
        new_messages.append(new_msg)

    return {"messages": new_messages}


def main():
    print(f"Loading {INPUT_FILE}...")

    examples = []
    errors = 0

    with open(INPUT_FILE, "r", encoding="utf-8") as f:
        for line_num, line in enumerate(f, 1):
            line = line.strip()
            if not line:
                continue
            try:
                obj = json.loads(line)
                examples.append(obj)
            except json.JSONDecodeError as e:
                errors += 1
                if errors <= 5:
                    print(f"[WARN] Line {line_num}: Invalid JSON")

    print(f"Loaded {len(examples)} examples ({errors} errors)")

    # Process examples
    populated = []
    templates_found = 0

    for obj in examples:
        # Check if this example has placeholders
        has_template = False
        for msg in obj.get("messages", []):
            if has_placeholders(msg.get("content", "")):
                has_template = True
                break

        if has_template:
            templates_found += 1
            # Populate the template
            new_obj = process_example(obj)
            populated.append(new_obj)
        else:
            # Keep as-is
            populated.append(obj)

    print(f"\nTemplates populated: {templates_found}")
    print(f"Total examples: {len(populated)}")

    # Write output
    print(f"\nWriting to {OUTPUT_FILE}...")
    with open(OUTPUT_FILE, "w", encoding="utf-8") as f:
        for obj in populated:
            f.write(json.dumps(obj) + "\n")

    print(f"Done! Output has {len(populated)} examples.")

    # Show sample populated template
    if templates_found > 0:
        print("\n" + "=" * 60)
        print("Sample populated template:")
        print("=" * 60)
        for obj in populated:
            content = ""
            for msg in obj.get("messages", []):
                if msg.get("role") == "assistant":
                    content = msg.get("content", "")
                    break
            if "runway" in content.lower() and "zero" in content.lower():
                print(json.dumps(obj, indent=2)[:500])
                break


if __name__ == "__main__":
    main()
