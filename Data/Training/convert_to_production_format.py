#!/usr/bin/env python3
"""
Convert training data to match the exact production format used by AeroAI.

Production format uses nested JSON:
- flight_info.callsign (not top-level callsign)
- clearance_decision.dep_runway (not top-level dep_runway)
- clearance_decision.sid (not top-level sid)
- clearance_decision.initial_altitude_ft (not top-level initial_altitude_ft)
- clearance_decision.squawk (not top-level squawk)
- ground_frequency_mhz (for ground handoff)

And wraps in CONTEXT_JSON format that BuildUserPrompt generates.
"""

import json
import os
import random

SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
INPUT_FILE = os.path.join(SCRIPT_DIR, "training_best_10usd.jsonl")
PHRASEOLOGY_FILE = os.path.join(SCRIPT_DIR, "training_phraseology_complete.jsonl")
OUTPUT_FILE = os.path.join(SCRIPT_DIR, "training_production_format.jsonl")

# Ground frequencies for common airports
GROUND_FREQUENCIES = {
    "EGLL": 121.7,
    "EGPH": 121.75,
    "EGCC": 121.85,
    "EGKK": 121.8,
    "EGLC": 121.825,
    "EGBB": 121.8,
    "EGNX": 121.9,
    "EGGW": 121.75,
    "EHAM": 121.8,
    "LFPG": 121.8,
    "EDDF": 121.85,
    "LEMD": 121.65,
    "LIRF": 121.65,
    "KJFK": 121.9,
    "KLAX": 121.65,
    "OMDB": 121.725,
    "VHHH": 121.6,
    "WSSS": 121.65,
}

def convert_to_production_format(old_entry):
    """Convert flat training format to nested production format."""
    try:
        messages = old_entry.get("messages", [])
        if len(messages) != 3:
            return None
        
        system_msg = messages[0].get("content", "")
        user_msg = messages[1].get("content", "")
        assistant_msg = messages[2].get("content", "")
        
        # Parse the user message to extract context and pilot transmission
        if "\nPILOT:" not in user_msg:
            return None
        
        parts = user_msg.split("\nPILOT:", 1)
        old_context_str = parts[0].strip()
        pilot_transmission = parts[1].strip() if len(parts) > 1 else ""
        
        # Parse the old flat context
        try:
            old_context = json.loads(old_context_str)
        except json.JSONDecodeError:
            return None
        
        # Build new nested context matching production format
        controller_role = old_context.get("controller_role", "DELIVERY")
        
        # Map controller role to phase
        phase_map = {
            "DELIVERY": "CLEARANCE",
            "GROUND": "TAXI_OUT",
            "TOWER": "LINEUP",
            "DEPARTURE": "CLIMB",
            "CENTER": "ENROUTE",
            "APPROACH": "DESCENT",
        }
        phase = phase_map.get(controller_role, "CLEARANCE")
        
        # Extract callsign
        callsign = old_context.get("callsign", "")
        
        # Get destination/origin info
        destination = old_context.get("destination", old_context.get("cleared_to", ""))
        dep_icao = old_context.get("airport", old_context.get("dep_icao", "EGPH"))
        
        # Build flight_info
        flight_info = {
            "callsign": callsign,
        }
        if destination:
            flight_info["arr_icao"] = destination
        flight_info["dep_icao"] = dep_icao
        
        cruise_level = old_context.get("cruise_level")
        if cruise_level:
            flight_info["cruise_level"] = cruise_level
        
        # Build clearance_decision
        clearance_decision = {}
        
        # Map clearance type
        if controller_role == "DELIVERY":
            clearance_decision["clearance_type"] = "IFR_CLEARANCE"
            clearance_decision["cleared_to"] = destination
        elif controller_role == "GROUND":
            clearance_decision["clearance_type"] = "TAXI"
        elif controller_role == "TOWER":
            if "takeoff" in pilot_transmission.lower() or "ready" in pilot_transmission.lower():
                clearance_decision["clearance_type"] = "TAKEOFF"
            elif "land" in pilot_transmission.lower() or "final" in pilot_transmission.lower():
                clearance_decision["clearance_type"] = "LANDING"
            else:
                clearance_decision["clearance_type"] = "LINEUP"
        elif controller_role == "DEPARTURE":
            clearance_decision["clearance_type"] = "CLIMB"
        elif controller_role == "CENTER":
            clearance_decision["clearance_type"] = "ENROUTE"
        elif controller_role == "APPROACH":
            clearance_decision["clearance_type"] = "APPROACH"
        
        # Copy runway - strip RW prefix for production format
        dep_runway = old_context.get("dep_runway", "")
        if dep_runway:
            if dep_runway.upper().startswith("RW"):
                dep_runway = dep_runway[2:]
            clearance_decision["dep_runway"] = dep_runway
        
        arr_runway = old_context.get("arr_runway", "")
        if arr_runway:
            if arr_runway.upper().startswith("RW"):
                arr_runway = arr_runway[2:]
            clearance_decision["arr_runway"] = arr_runway
        
        # Copy SID
        sid = old_context.get("sid")
        if sid:
            clearance_decision["sid"] = sid
        
        # Copy altitude
        initial_altitude = old_context.get("initial_altitude_ft", old_context.get("initial_altitude"))
        if initial_altitude:
            try:
                clearance_decision["initial_altitude_ft"] = int(str(initial_altitude).replace(" FT", "").replace("FT", "").strip())
            except ValueError:
                pass
        
        cleared_altitude = old_context.get("cleared_altitude_ft")
        if cleared_altitude:
            clearance_decision["cleared_altitude_ft"] = cleared_altitude
        
        # Copy squawk
        squawk = old_context.get("squawk")
        if squawk:
            clearance_decision["squawk"] = str(squawk)
        
        # Copy heading
        heading = old_context.get("cleared_heading_deg")
        if heading:
            clearance_decision["cleared_heading_deg"] = heading
        
        # Copy speed
        speed = old_context.get("speed_restriction_kt")
        if speed:
            clearance_decision["speed_restriction_kt"] = speed
        
        # Via radar vectors
        if old_context.get("via_radar_vectors"):
            clearance_decision["via_radar_vectors"] = True
        
        # Build the new context
        new_context = {
            "controller_role": controller_role,
            "phase": phase,
            "flight_info": flight_info,
            "clearance_decision": clearance_decision,
        }
        
        # Add ground frequency for delivery (for handoff after readback)
        if controller_role == "DELIVERY" and dep_icao in GROUND_FREQUENCIES:
            new_context["ground_frequency_mhz"] = GROUND_FREQUENCIES[dep_icao]
        
        # Build the user prompt in EXACT production format
        context_json = json.dumps(new_context, indent=2)
        
        user_prompt = f"""CONTEXT_JSON:
```json
{context_json}
```

PILOT_TRANSMISSION:
"{pilot_transmission}"

Using ONLY this information and obeying your role and permissions, respond with a single ICAO-style ATC transmission."""
        
        # Build new training entry
        new_entry = {
            "messages": [
                {"role": "system", "content": "You are AeroAI ATC."},
                {"role": "user", "content": user_prompt},
                {"role": "assistant", "content": assistant_msg}
            ]
        }
        
        return new_entry
        
    except Exception as e:
        print(f"Error converting entry: {e}")
        return None


def main():
    print("Converting training data to production format...")
    
    converted = []
    failed = 0
    
    # Process main training file
    if os.path.exists(INPUT_FILE):
        with open(INPUT_FILE, 'r', encoding='utf-8') as f:
            for line_num, line in enumerate(f, 1):
                line = line.strip()
                if not line:
                    continue
                try:
                    entry = json.loads(line)
                    new_entry = convert_to_production_format(entry)
                    if new_entry:
                        converted.append(new_entry)
                    else:
                        failed += 1
                except json.JSONDecodeError:
                    failed += 1
        print(f"Processed {INPUT_FILE}: {len(converted)} converted, {failed} failed")
    
    # Process phraseology file
    phraseology_count = 0
    if os.path.exists(PHRASEOLOGY_FILE):
        with open(PHRASEOLOGY_FILE, 'r', encoding='utf-8') as f:
            for line in f:
                line = line.strip()
                if not line:
                    continue
                try:
                    entry = json.loads(line)
                    new_entry = convert_to_production_format(entry)
                    if new_entry:
                        converted.append(new_entry)
                        phraseology_count += 1
                except json.JSONDecodeError:
                    pass
        print(f"Processed {PHRASEOLOGY_FILE}: {phraseology_count} converted")
    
    # Write output
    with open(OUTPUT_FILE, 'w', encoding='utf-8') as f:
        for entry in converted:
            f.write(json.dumps(entry) + "\n")
    
    print(f"\nTotal: {len(converted)} examples written to {OUTPUT_FILE}")
    print("Ready for fine-tuning with production format!")
    
    # Show a sample
    if converted:
        print("\n--- Sample Entry ---")
        sample = converted[0]
        print(f"System: {sample['messages'][0]['content'][:50]}...")
        print(f"User: {sample['messages'][1]['content'][:200]}...")
        print(f"Assistant: {sample['messages'][2]['content']}")


if __name__ == "__main__":
    main()

