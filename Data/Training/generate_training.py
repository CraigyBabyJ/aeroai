"""
AeroAI Training Data Generator
Generates high-quality, varied JSONL training data for fine-tuning.
"""

import json
import random

OUTPUT_FILE = "training_aeroai_generated.jsonl"

# ============================================================================
# PHONETICS
# ============================================================================

PHONETIC_NUMBERS = {
    "0": "zero", "1": "one", "2": "two", "3": "three", "4": "four",
    "5": "five", "6": "six", "7": "seven", "8": "eight", "9": "niner"
}

PHONETIC_LETTERS = {
    "A": "alpha", "B": "bravo", "C": "charlie", "D": "delta", "E": "echo",
    "F": "foxtrot", "G": "golf", "H": "hotel", "I": "india", "J": "juliet",
    "K": "kilo", "L": "lima", "M": "mike", "N": "november", "O": "oscar",
    "P": "papa", "Q": "quebec", "R": "romeo", "S": "sierra", "T": "tango",
    "U": "uniform", "V": "victor", "W": "whiskey", "X": "x-ray", "Y": "yankee",
    "Z": "zulu"
}

def speak_number(num_str: str) -> str:
    """Convert '4271' to 'four two seven one'"""
    return " ".join(PHONETIC_NUMBERS.get(c, c) for c in str(num_str))

def speak_runway(rwy: str) -> str:
    """Convert '27R' to 'two seven right'"""
    result = []
    for c in rwy:
        if c in PHONETIC_NUMBERS:
            result.append(PHONETIC_NUMBERS[c])
        elif c == "L":
            result.append("left")
        elif c == "R":
            result.append("right")
        elif c == "C":
            result.append("centre")
    return " ".join(result)

def speak_altitude(alt: str) -> str:
    """Convert '5000' to 'five thousand feet' or 'FL350' to 'flight level three five zero'"""
    if alt.startswith("FL"):
        return f"flight level {speak_number(alt[2:])}"
    elif int(alt) >= 10000:
        return f"{speak_number(alt)} feet"
    else:
        thousands = int(alt) // 1000
        return f"{PHONETIC_NUMBERS[str(thousands)]} thousand feet"

def speak_callsign_suffix(suffix: str) -> str:
    """Convert '123' or '12X' to 'one two three' or 'one two x-ray'"""
    result = []
    for c in suffix.upper():
        if c in PHONETIC_NUMBERS:
            result.append(PHONETIC_NUMBERS[c])
        elif c in PHONETIC_LETTERS:
            result.append(PHONETIC_LETTERS[c])
    return " ".join(result)

def speak_sid(sid: str) -> str:
    """Convert 'LAM3F' to 'LAM three Foxtrot'"""
    if not sid:
        return ""
    result = []
    for c in sid.upper():
        if c in PHONETIC_NUMBERS:
            result.append(PHONETIC_NUMBERS[c])
        elif c in PHONETIC_LETTERS:
            result.append(PHONETIC_LETTERS[c].capitalize())
        else:
            result.append(c)
    return " ".join(result)

def speak_heading(hdg: str) -> str:
    """Convert '320' to 'three two zero'"""
    return speak_number(hdg.zfill(3))

# ============================================================================
# DATA POOLS
# ============================================================================

AIRLINES = [
    ("BAW", "Speedbird"),
    ("EZY", "EASY"),
    ("RYR", "Ryanair"),
    ("DLH", "Lufthansa"),
    ("AFR", "Air France"),
    ("SAS", "Scandinavian"),
    ("EIN", "Shamrock"),
    ("KLM", "KLM"),
    ("UAE", "Emirates"),
    ("QTR", "Qatari"),
    ("THY", "Turkish"),
    ("IBE", "Iberia"),
    ("AZA", "Alitalia"),
    ("SWR", "Swiss"),
    ("AUA", "Austrian"),
    ("TAP", "TAP"),
    ("VIR", "Virgin"),
    ("NAX", "Norwegian"),
    ("FIN", "Finnair"),
    ("LOT", "LOT"),
]

DESTINATIONS = [
    ("EGLL", "Heathrow"),
    ("EGKK", "Gatwick"),
    ("EHAM", "Amsterdam"),
    ("LFPG", "Paris Charles de Gaulle"),
    ("EDDF", "Frankfurt"),
    ("LEMD", "Madrid"),
    ("LIRF", "Rome"),
    ("EBBR", "Brussels"),
    ("LSZH", "Zurich"),
    ("EDDM", "Munich"),
    ("LOWW", "Vienna"),
    ("EKCH", "Copenhagen"),
    ("ESSA", "Stockholm"),
    ("LEBL", "Barcelona"),
    ("LPPT", "Lisbon"),
]

RUNWAYS = ["09", "09L", "09R", "27", "27L", "27R", "22L", "22R", "04", "04L", "04R", "26", "26L", "26R", "32", "14"]

SIDS = [
    "LAM3F", "BPK2J", "DAYNE2G", "CPT4K", "WOBUN3G", "SAM2P", "TIGER1A",
    "MAXIT1J", "DET2K", "UMLAT1G", "BOGNA1M", "HARDY5M", "KENET3B"
]

FIXES = ["TUXIL", "LONAM", "SUGOL", "DET", "TIMBA", "WILLO", "KENET", "BOGNA", "HARDY", "TIGER", "WOBUN"]

STANDS = list(range(1, 150))

SQUAWKS = [f"{random.randint(0,7)}{random.randint(0,7)}{random.randint(0,7)}{random.randint(0,7)}" for _ in range(50)]

HEADINGS = ["010", "030", "060", "090", "120", "150", "180", "210", "240", "270", "300", "330", "360"]

WIND_DIRS = ["240", "270", "300", "320", "180", "090", "060"]
WIND_SPEEDS = ["8", "12", "15", "18", "22", "25"]

# ============================================================================
# PHRASEOLOGY VARIATIONS
# ============================================================================

def random_callsign():
    """Generate callsign code and spoken version"""
    code, airline = random.choice(AIRLINES)
    suffix = str(random.randint(1, 999)) + random.choice(["", "", "", "A", "B", "C", "P", "X", "K", "M"])
    suffix = suffix.strip()
    spoken = f"{airline} {speak_callsign_suffix(suffix)}"
    return f"{code}{suffix}", spoken

def delivery_pilot_phrases(spoken: str):
    return random.choice([
        f"Delivery, {spoken}, request IFR clearance",
        f"{spoken}, request clearance",
        f"Delivery, {spoken}, requesting IFR to {{dest}}",
        f"{spoken}, stand {{stand}}, information {{atis}}, request IFR clearance",
        f"Delivery, {spoken}, with information {{atis}}, request clearance to {{dest}}",
        f"{spoken}, request IFR clearance to {{dest}}",
    ])

def delivery_atc_responses(spoken: str, dest_name: str, sid: str, runway: str, altitude: str, squawk: str):
    sid_part = f"via {speak_sid(sid)} departure" if sid else "via radar vectors"
    rwy_spoken = speak_runway(runway)
    alt_spoken = speak_altitude(altitude)
    sqk_spoken = speak_number(squawk)
    
    return random.choice([
        f"{spoken}, cleared to {dest_name} {sid_part}, runway {rwy_spoken}. Climb initially {alt_spoken}. Squawk {sqk_spoken}.",
        f"{spoken}, cleared to {dest_name} {sid_part}. Runway {rwy_spoken}, climb initially {alt_spoken}, squawk {sqk_spoken}.",
        f"{spoken}, you're cleared to {dest_name} {sid_part}, runway {rwy_spoken}, initial climb {alt_spoken}. Squawk {sqk_spoken}.",
        f"{spoken}, clearance, {dest_name}, {sid_part}, runway {rwy_spoken}. Climb {alt_spoken}. Squawk {sqk_spoken}.",
    ])

def ground_push_pilot_phrases(spoken: str):
    return random.choice([
        f"Ground, {spoken}, request push and start",
        f"{spoken}, request push",
        f"Ground, {spoken}, ready for push and start",
        f"{spoken}, stand {{stand}}, request startup",
        f"Ground, {spoken}, request pushback",
    ])

def ground_push_atc_responses(spoken: str):
    facing = random.choice(["north", "south", "east", "west", "facing north", "facing south", "face east", "face west"])
    return random.choice([
        f"{spoken}, push and start approved, {facing}.",
        f"{spoken}, pushback approved, {facing}.",
        f"{spoken}, push approved, {facing}. Report ready for taxi.",
        f"{spoken}, start approved, push when ready, {facing}.",
    ])

def ground_taxi_pilot_phrases(spoken: str):
    return random.choice([
        f"Ground, {spoken}, request taxi",
        f"{spoken}, ready for taxi",
        f"Ground, {spoken}, request taxi to runway {{runway}}",
        f"{spoken}, taxi",
    ])

def ground_taxi_atc_responses(spoken: str, runway: str):
    rwy_spoken = speak_runway(runway)
    taxiways = random.choice(["alpha", "bravo", "charlie", "delta", "echo", "november", "mike", "lima", "kilo"])
    hold_short = random.choice([
        f"Hold short runway {rwy_spoken}.",
        f"Report holding point.",
        f"Give way to company 737 on your right.",
        "",
    ])
    return random.choice([
        f"{spoken}, taxi to holding point runway {rwy_spoken} via {taxiways}. {hold_short}".strip(),
        f"{spoken}, taxi holding point {rwy_spoken}. {hold_short}".strip(),
        f"{spoken}, taxi to runway {rwy_spoken} via {taxiways}. {hold_short}".strip(),
    ])

def tower_ready_pilot_phrases(spoken: str):
    return random.choice([
        f"Tower, {spoken}, ready",
        f"{spoken}, ready for departure",
        f"Tower, {spoken}, holding point runway {{runway}}, ready",
        f"{spoken}, ready",
    ])

def tower_lineup_atc_responses(spoken: str, runway: str):
    rwy_spoken = speak_runway(runway)
    return random.choice([
        f"{spoken}, line up and wait runway {rwy_spoken}.",
        f"{spoken}, line up runway {rwy_spoken}.",
        f"{spoken}, behind the landing traffic, line up runway {rwy_spoken} behind.",
        f"{spoken}, runway {rwy_spoken}, line up and wait.",
    ])

def tower_takeoff_atc_responses(spoken: str, runway: str):
    rwy_spoken = speak_runway(runway)
    wind = f"wind {random.choice(WIND_DIRS)} degrees {random.choice(WIND_SPEEDS)} knots"
    return random.choice([
        f"{spoken}, runway {rwy_spoken}, cleared for take-off.",
        f"{spoken}, {wind}, runway {rwy_spoken}, cleared for take-off.",
        f"{spoken}, cleared take-off runway {rwy_spoken}.",
        f"{spoken}, runway {rwy_spoken}, cleared immediate take-off, traffic on short final.",
    ])

def tower_landing_pilot_phrases(spoken: str):
    return random.choice([
        f"Tower, {spoken}, final runway {{runway}}",
        f"{spoken}, four miles final",
        f"Tower, {spoken}, established ILS runway {{runway}}",
        f"{spoken}, short final",
    ])

def tower_landing_atc_responses(spoken: str, runway: str):
    rwy_spoken = speak_runway(runway)
    wind = f"Wind {random.choice(WIND_DIRS)} degrees {random.choice(WIND_SPEEDS)} knots."
    return random.choice([
        f"{spoken}, runway {rwy_spoken}, cleared to land.",
        f"{spoken}, runway {rwy_spoken}, cleared to land. {wind}",
        f"{spoken}, continue approach runway {rwy_spoken}, cleared to land.",
        f"{spoken}, number one, runway {rwy_spoken}, cleared to land.",
    ])

def departure_pilot_phrases(spoken: str):
    alt = random.choice(["two thousand", "three thousand", "four thousand", "passing two thousand five hundred"])
    return random.choice([
        f"Departure, {spoken}, passing {alt}",
        f"{spoken}, airborne",
        f"Departure, {spoken}, with you passing {alt}",
        f"{spoken}, climbing through {alt}",
    ])

def departure_atc_responses(spoken: str, altitude: str):
    alt_spoken = speak_altitude(altitude)
    heading = random.choice(HEADINGS)
    hdg_spoken = speak_heading(heading)
    return random.choice([
        f"{spoken}, radar contact. Continue climb {alt_spoken}.",
        f"{spoken}, identified. Climb {alt_spoken}.",
        f"{spoken}, radar contact. Turn right heading {hdg_spoken}, climb {alt_spoken}.",
        f"{spoken}, continue climb {alt_spoken}, report level.",
    ])

def center_pilot_phrases(spoken: str):
    level = random.choice(["FL350", "FL370", "FL390", "FL310", "FL280"])
    return random.choice([
        f"{spoken}, level {speak_altitude(level).replace('flight level ', 'flight level ')}",
        f"Center, {spoken}, request direct {{fix}}",
        f"{spoken}, request higher",
        f"Center, {spoken}, requesting weather deviation",
    ])

def center_atc_responses(spoken: str, fix: str = None):
    new_level = random.choice(["FL350", "FL370", "FL390", "FL410"])
    return random.choice([
        f"{spoken}, roger.",
        f"{spoken}, climb {speak_altitude(new_level)}.",
        f"{spoken}, direct {fix} approved." if fix else f"{spoken}, roger, maintain.",
        f"{spoken}, deviation approved, report back on track.",
    ])

def approach_pilot_phrases(spoken: str):
    return random.choice([
        f"Approach, {spoken}, descending {{altitude}}",
        f"{spoken}, established localiser runway {{runway}}",
        f"Approach, {spoken}, field in sight",
        f"{spoken}, request vectors ILS runway {{runway}}",
    ])

def approach_atc_responses(spoken: str, runway: str):
    rwy_spoken = speak_runway(runway)
    heading = random.choice(HEADINGS)
    hdg_spoken = speak_heading(heading)
    return random.choice([
        f"{spoken}, turn left heading {hdg_spoken}, vectors ILS runway {rwy_spoken}.",
        f"{spoken}, cleared ILS approach runway {rwy_spoken}.",
        f"{spoken}, cleared visual approach runway {rwy_spoken}.",
        f"{spoken}, descend three thousand feet, expect ILS runway {rwy_spoken}.",
        f"{spoken}, contact Tower {random.choice(['one one eight decimal five', 'one one niner decimal seven', 'one two zero decimal four'])}.",
    ])

def readback_correct_atc(spoken: str):
    return random.choice([
        f"{spoken}, readback correct.",
        f"{spoken}, correct.",
        f"{spoken}, that's correct.",
        f"Readback correct, {spoken}.",
    ])

def readback_incorrect_atc(spoken: str, correction: str):
    return random.choice([
        f"{spoken}, negative. {correction}",
        f"Negative {spoken}, {correction}",
        f"{spoken}, incorrect. {correction}",
    ])

def radio_check_atc(spoken: str):
    return random.choice([
        f"{spoken}, loud and clear.",
        f"{spoken}, reading you five.",
        f"{spoken}, five by five.",
        f"{spoken}, read you loud and clear.",
    ])

def standby_atc(spoken: str):
    return random.choice([
        f"{spoken}, standby.",
        f"{spoken}, hold position, standby.",
        f"Standby {spoken}.",
        f"{spoken}, expect delay.",
    ])

def goaround_atc(spoken: str, runway: str):
    rwy_spoken = speak_runway(runway)
    return random.choice([
        f"{spoken}, go around. Climb straight ahead to three thousand feet.",
        f"{spoken}, go around, runway {rwy_spoken}. Climb heading runway to three thousand.",
        f"{spoken}, go around. I say again, go around. Traffic on runway.",
    ])

def emergency_atc(spoken: str, emergency_type: str):
    if emergency_type == "MAYDAY":
        return random.choice([
            f"{spoken}, mayday acknowledged. State nature of emergency and intentions.",
            f"{spoken}, mayday copied. Turn left heading two seven zero for immediate approach. Emergency services alerted.",
            f"All stations, all stations, {spoken} is declaring mayday. Standby.",
        ])
    else:  # PAN
        return random.choice([
            f"{spoken}, pan acknowledged. State your request.",
            f"{spoken}, pan copied. Priority handling approved. State intentions.",
        ])

# ============================================================================
# EXAMPLE GENERATORS
# ============================================================================

def gen_delivery_clearance():
    callsign, spoken = random_callsign()
    dest_icao, dest_name = random.choice(DESTINATIONS)
    sid = random.choice(SIDS + [None, None])  # More chance of None
    runway = random.choice(RUNWAYS)
    altitude = random.choice(["4000", "5000", "6000"])
    squawk = f"{random.randint(1,7)}{random.randint(0,7)}{random.randint(0,7)}{random.randint(0,7)}"
    stand = random.choice(STANDS)
    atis = random.choice(["alpha", "bravo", "charlie", "delta", "echo", "foxtrot", "golf", "hotel", "kilo"])
    
    pilot = delivery_pilot_phrases(spoken).format(dest=dest_name, stand=stand, atis=atis)
    atc = delivery_atc_responses(spoken, dest_name, sid, runway, altitude, squawk)
    
    context = {
        "controller_role": "DELIVERY",
        "callsign": callsign,
        "clearance_type": "IFR",
        "cleared_to": dest_icao,
        "sid": sid,
        "dep_runway": runway,
        "initial_altitude": altitude,
        "squawk": squawk,
    }
    if sid is None:
        context["via_radar_vectors"] = True
        del context["sid"]
    
    return context, pilot, atc

def gen_delivery_readback_correct():
    callsign, spoken = random_callsign()
    dest_icao, dest_name = random.choice(DESTINATIONS)
    sid = random.choice(SIDS)
    runway = random.choice(RUNWAYS)
    altitude = random.choice(["4000", "5000", "6000"])
    squawk = f"{random.randint(1,7)}{random.randint(0,7)}{random.randint(0,7)}{random.randint(0,7)}"
    
    # Pilot reads back correctly
    sid_spoken = speak_sid(sid) if sid else "radar vectors"
    rwy_spoken = speak_runway(runway)
    alt_spoken = speak_altitude(altitude)
    sqk_spoken = speak_number(squawk)
    
    pilot = f"Cleared to {dest_name} {sid_spoken}, runway {rwy_spoken}, climb {alt_spoken}, squawk {sqk_spoken}, {spoken}"
    atc = readback_correct_atc(spoken)
    
    context = {
        "controller_role": "DELIVERY",
        "callsign": callsign,
        "clearance_type": "IFR",
        "cleared_to": dest_icao,
        "sid": sid,
        "dep_runway": runway,
        "initial_altitude": altitude,
        "squawk": squawk,
    }
    return context, pilot, atc

def gen_delivery_readback_incorrect():
    callsign, spoken = random_callsign()
    dest_icao, dest_name = random.choice(DESTINATIONS)
    sid = random.choice(SIDS)
    runway = random.choice(RUNWAYS)
    altitude = random.choice(["4000", "5000", "6000"])
    squawk = f"{random.randint(1,7)}{random.randint(0,7)}{random.randint(0,7)}{random.randint(0,7)}"
    
    # Pilot makes a mistake
    wrong_type = random.choice(["squawk", "altitude", "runway"])
    
    if wrong_type == "squawk":
        wrong_squawk = f"{random.randint(1,7)}{random.randint(0,7)}{random.randint(0,7)}{random.randint(0,7)}"
        pilot = f"{spoken}, squawk {speak_number(wrong_squawk)}"
        atc = readback_incorrect_atc(spoken, f"Squawk {speak_number(squawk)}.")
    elif wrong_type == "altitude":
        wrong_alt = random.choice(["3000", "4000", "5000", "6000", "7000"])
        while wrong_alt == altitude:
            wrong_alt = random.choice(["3000", "4000", "5000", "6000", "7000"])
        pilot = f"{spoken}, climb {speak_altitude(wrong_alt)}"
        atc = readback_incorrect_atc(spoken, f"Climb initially {speak_altitude(altitude)}.")
    else:
        wrong_rwy = random.choice(RUNWAYS)
        while wrong_rwy == runway:
            wrong_rwy = random.choice(RUNWAYS)
        pilot = f"{spoken}, runway {speak_runway(wrong_rwy)}"
        atc = readback_incorrect_atc(spoken, f"Runway {speak_runway(runway)}.")
    
    context = {
        "controller_role": "DELIVERY",
        "callsign": callsign,
        "clearance_type": "IFR",
        "cleared_to": dest_icao,
        "sid": sid,
        "dep_runway": runway,
        "initial_altitude": altitude,
        "squawk": squawk,
    }
    return context, pilot, atc

def gen_ground_push():
    callsign, spoken = random_callsign()
    stand = random.choice(STANDS)
    
    pilot = ground_push_pilot_phrases(spoken).format(stand=stand)
    atc = ground_push_atc_responses(spoken)
    
    context = {"controller_role": "GROUND", "callsign": callsign}
    return context, pilot, atc

def gen_ground_taxi():
    callsign, spoken = random_callsign()
    runway = random.choice(RUNWAYS)
    
    pilot = ground_taxi_pilot_phrases(spoken).format(runway=speak_runway(runway))
    atc = ground_taxi_atc_responses(spoken, runway)
    
    context = {"controller_role": "GROUND", "callsign": callsign, "dep_runway": runway}
    return context, pilot, atc

def gen_tower_ready():
    callsign, spoken = random_callsign()
    runway = random.choice(RUNWAYS)
    
    pilot = tower_ready_pilot_phrases(spoken).format(runway=speak_runway(runway))
    atc = tower_lineup_atc_responses(spoken, runway)
    
    context = {"controller_role": "TOWER", "callsign": callsign, "dep_runway": runway}
    return context, pilot, atc

def gen_tower_takeoff():
    callsign, spoken = random_callsign()
    runway = random.choice(RUNWAYS)
    
    pilot = f"{spoken}, lined up runway {speak_runway(runway)}"
    atc = tower_takeoff_atc_responses(spoken, runway)
    
    context = {"controller_role": "TOWER", "callsign": callsign, "dep_runway": runway}
    return context, pilot, atc

def gen_tower_landing():
    callsign, spoken = random_callsign()
    runway = random.choice(RUNWAYS)
    
    pilot = tower_landing_pilot_phrases(spoken).format(runway=speak_runway(runway))
    atc = tower_landing_atc_responses(spoken, runway)
    
    context = {"controller_role": "TOWER", "callsign": callsign, "arrival_runway": runway}
    return context, pilot, atc

def gen_tower_goaround():
    callsign, spoken = random_callsign()
    runway = random.choice(RUNWAYS)
    
    reason = random.choice(["unstable approach", "runway not clear", "go around"])
    pilot = f"{spoken}, {reason}"
    atc = goaround_atc(spoken, runway)
    
    context = {"controller_role": "TOWER", "callsign": callsign, "arrival_runway": runway}
    return context, pilot, atc

def gen_departure():
    callsign, spoken = random_callsign()
    altitude = random.choice(["5000", "6000", "FL100", "FL120"])
    
    pilot = departure_pilot_phrases(spoken)
    atc = departure_atc_responses(spoken, altitude)
    
    context = {"controller_role": "DEPARTURE", "callsign": callsign, "initial_altitude": altitude}
    return context, pilot, atc

def gen_departure_handoff():
    callsign, spoken = random_callsign()
    
    pilot = f"{spoken}, level flight level two four zero"
    freq = random.choice(["one three two decimal six", "one two niner decimal four", "one three four decimal two five"])
    atc = f"{spoken}, contact London Control {freq}."
    
    context = {"controller_role": "DEPARTURE", "callsign": callsign}
    return context, pilot, atc

def gen_center():
    callsign, spoken = random_callsign()
    fix = random.choice(FIXES)
    
    pilot = center_pilot_phrases(spoken).format(fix=fix)
    atc = center_atc_responses(spoken, fix)
    
    context = {"controller_role": "CENTER", "callsign": callsign}
    return context, pilot, atc

def gen_approach():
    callsign, spoken = random_callsign()
    runway = random.choice(RUNWAYS)
    altitude = random.choice(["5000", "6000", "4000"])
    
    pilot = approach_pilot_phrases(spoken).format(runway=speak_runway(runway), altitude=speak_altitude(altitude))
    atc = approach_atc_responses(spoken, runway)
    
    context = {"controller_role": "APPROACH", "callsign": callsign, "arrival_runway": runway}
    return context, pilot, atc

def gen_radio_check():
    callsign, spoken = random_callsign()
    role = random.choice(["GROUND", "TOWER", "DEPARTURE", "CENTER", "APPROACH"])
    
    pilot = random.choice([
        f"{role.capitalize()}, {spoken}, radio check",
        f"{spoken}, radio check",
        f"{role.capitalize()}, {spoken}, how do you read",
    ])
    atc = radio_check_atc(spoken)
    
    context = {"controller_role": role, "callsign": callsign}
    return context, pilot, atc

def gen_standby():
    callsign, spoken = random_callsign()
    role = random.choice(["DELIVERY", "GROUND", "TOWER"])
    
    pilot = random.choice([
        f"{role.capitalize()}, {spoken}, request {{request}}",
        f"{spoken}, {{request}}",
    ]).format(request=random.choice(["taxi", "clearance", "push and start", "departure"]))
    atc = standby_atc(spoken)
    
    context = {"controller_role": role, "callsign": callsign}
    return context, pilot, atc

def gen_emergency():
    callsign, spoken = random_callsign()
    emergency_type = random.choice(["MAYDAY", "PAN"])
    
    if emergency_type == "MAYDAY":
        reason = random.choice(["engine fire", "engine failure", "medical emergency", "fuel emergency", "hydraulic failure"])
        pilot = f"Mayday mayday mayday, {spoken}, {reason}"
    else:
        reason = random.choice(["passenger medical", "minor technical issue", "diversion request", "low fuel"])
        pilot = f"Pan pan pan pan, {spoken}, {reason}"
    
    atc = emergency_atc(spoken, emergency_type)
    
    context = {"controller_role": random.choice(["TOWER", "APPROACH", "DEPARTURE", "CENTER"]), "callsign": callsign, "emergency": emergency_type}
    return context, pilot, atc

def gen_say_again():
    callsign, spoken = random_callsign()
    role = random.choice(["DELIVERY", "GROUND", "TOWER", "DEPARTURE", "CENTER", "APPROACH"])
    
    pilot = random.choice([
        f"{spoken}, say again",
        f"Say again {role.lower()}",
        f"{spoken}, didn't copy",
    ])
    atc = f"{spoken}, I say again, " + random.choice([
        "taxi to holding point runway two seven right.",
        "cleared to land runway two seven left.",
        "climb flight level three five zero.",
        "turn right heading zero niner zero.",
    ])
    
    context = {"controller_role": role, "callsign": callsign}
    return context, pilot, atc

# ============================================================================
# MAIN GENERATOR
# ============================================================================

GENERATORS = [
    (gen_delivery_clearance, 80),        # Common
    (gen_delivery_readback_correct, 40), # Common
    (gen_delivery_readback_incorrect, 30), # Important edge case
    (gen_ground_push, 50),               # Common
    (gen_ground_taxi, 50),               # Common
    (gen_tower_ready, 40),               # Common
    (gen_tower_takeoff, 40),             # Common
    (gen_tower_landing, 50),             # Common
    (gen_tower_goaround, 20),            # Less common but important
    (gen_departure, 40),                 # Common
    (gen_departure_handoff, 20),         # Common
    (gen_center, 30),                    # Common
    (gen_approach, 40),                  # Common
    (gen_radio_check, 30),               # Common
    (gen_standby, 20),                   # Less common
    (gen_emergency, 15),                 # Rare but critical
    (gen_say_again, 15),                 # Less common
]

def main():
    examples = []
    
    for generator, count in GENERATORS:
        for _ in range(count):
            context, pilot, atc = generator()
            examples.append({
                "messages": [
                    {"role": "system", "content": "You are AeroAI ATC."},
                    {"role": "user", "content": json.dumps(context) + "\nPILOT: " + pilot},
                    {"role": "assistant", "content": atc}
                ]
            })
    
    random.shuffle(examples)
    
    with open(OUTPUT_FILE, "w", encoding="utf-8") as f:
        for ex in examples:
            f.write(json.dumps(ex) + "\n")
    
    print(f"Generated {len(examples)} training examples → {OUTPUT_FILE}")
    print(f"\nBreakdown:")
    for gen, count in GENERATORS:
        print(f"  {gen.__name__}: {count}")

if __name__ == "__main__":
    main()

