#!/usr/bin/env python3
"""
Select the best training examples for a budget-constrained fine-tuning run.
Target: ~$10 worth (~3500 high-quality examples)
"""

import json
import os
import re
from collections import defaultdict

SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
INPUT_FILE = os.path.join(SCRIPT_DIR, "training_aeroai.json")
OUTPUT_FILE = os.path.join(SCRIPT_DIR, "training_best_10usd.jsonl")

def estimate_tokens(text):
    """Rough token estimate (4 chars per token average)"""
    return len(text) // 4

def score_example(entry):
    """Score an example for quality (higher = better)"""
    score = 0
    
    try:
        messages = entry.get("messages", [])
        if len(messages) != 3:
            return -1  # Invalid structure
        
        system_msg = messages[0].get("content", "")
        user_msg = messages[1].get("content", "")
        assistant_msg = messages[2].get("content", "")
        
        # Must have AeroAI system message
        if "AeroAI" not in system_msg:
            return -1
        
        # Must have PILOT message
        if "PILOT:" not in user_msg:
            return -1
        
        # Must have assistant response
        if not assistant_msg or len(assistant_msg) < 10:
            return -1
        
        # No placeholders/templates
        if re.search(r'\{[A-Z_]+\}', user_msg + assistant_msg):
            return -1
        
        # QUALITY SCORING
        
        # Proper phonetic numbers in response (good)
        phonetics = ['zero', 'one', 'two', 'three', 'four', 'five', 'six', 'seven', 'eight', 'niner']
        for p in phonetics:
            if p in assistant_msg.lower():
                score += 2
        
        # Proper runway pronunciation (two seven right, etc)
        if re.search(r'(two|one|zero|three)\s+(zero|one|two|three|four|five|six|seven|eight|niner)\s*(left|right|center)?', assistant_msg.lower()):
            score += 5
        
        # Has clearance elements
        if 'cleared' in assistant_msg.lower():
            score += 3
        if 'squawk' in assistant_msg.lower():
            score += 3
        if 'climb' in assistant_msg.lower():
            score += 2
        if 'runway' in assistant_msg.lower():
            score += 2
        if 'departure' in assistant_msg.lower():
            score += 2
        
        # Has proper SID pronunciation (letters spelled out)
        if re.search(r'(alpha|bravo|charlie|delta|echo|foxtrot|golf|hotel|india|juliet|kilo|lima|mike|november|oscar|papa|quebec|romeo|sierra|tango|uniform|victor|whiskey|x-ray|yankee|zulu)', assistant_msg.lower()):
            score += 3
        
        # Penalize raw numbers (bad format)
        if re.search(r'runway\s+\d{2}[LRC]?(?!\s)', assistant_msg):
            score -= 5
        
        # Penalize squawk with raw digits
        if re.search(r'squawk\s+\d{4}(?!\s)', assistant_msg):
            score -= 3
        
        # Reasonable length response (not too short, not too long)
        if 30 < len(assistant_msg) < 300:
            score += 3
        elif len(assistant_msg) < 20:
            score -= 5
        
        # Has context JSON (structured input)
        if '"controller_role"' in user_msg:
            score += 2
        
        # Extract controller role for diversity tracking
        role_match = re.search(r'"controller_role":\s*"([^"]+)"', user_msg)
        
        return score
        
    except Exception:
        return -1

def get_metadata(entry):
    """Extract metadata for diversity selection"""
    messages = entry.get("messages", [])
    user_msg = messages[1].get("content", "") if len(messages) > 1 else ""
    
    metadata = {
        "role": "UNKNOWN",
        "phase": "UNKNOWN", 
        "airport": "UNKNOWN",
        "accent": "UNKNOWN"
    }
    
    role_match = re.search(r'"controller_role":\s*"([^"]+)"', user_msg)
    if role_match:
        metadata["role"] = role_match.group(1)
    
    phase_match = re.search(r'"phase":\s*"([^"]+)"', user_msg)
    if phase_match:
        metadata["phase"] = phase_match.group(1)
    
    airport_match = re.search(r'"airport":\s*"([^"]+)"', user_msg)
    if airport_match:
        metadata["airport"] = airport_match.group(1)
        
    accent_match = re.search(r'"accent":\s*"([^"]+)"', user_msg)
    if accent_match:
        metadata["accent"] = accent_match.group(1)
    
    return metadata

def main():
    print("Loading training data...")
    
    examples = []
    with open(INPUT_FILE, 'r', encoding='utf-8') as f:
        for line_num, line in enumerate(f, 1):
            line = line.strip()
            if not line:
                continue
            try:
                entry = json.loads(line)
                score = score_example(entry)
                if score >= 0:  # Only keep valid examples
                    meta = get_metadata(entry)
                    tokens = estimate_tokens(json.dumps(entry))
                    examples.append({
                        "entry": entry,
                        "score": score,
                        "meta": meta,
                        "tokens": tokens,
                        "line": line_num
                    })
            except json.JSONDecodeError:
                continue
    
    print(f"Loaded {len(examples)} valid examples")
    
    # Sort by score (best first)
    examples.sort(key=lambda x: x["score"], reverse=True)
    
    # Select diverse high-quality examples
    # Target: ~3500 examples for $10 budget
    TARGET_EXAMPLES = 3500
    TARGET_TOKENS = 1_250_000  # ~$10 worth
    
    selected = []
    total_tokens = 0
    
    # Track diversity
    role_counts = defaultdict(int)
    airport_counts = defaultdict(int)
    accent_counts = defaultdict(int)
    
    # Diversity limits per category
    MAX_PER_ROLE = 800
    MAX_PER_AIRPORT = 200
    MAX_PER_ACCENT = 600
    
    for ex in examples:
        if len(selected) >= TARGET_EXAMPLES:
            break
        if total_tokens >= TARGET_TOKENS:
            break
            
        meta = ex["meta"]
        
        # Check diversity limits
        if role_counts[meta["role"]] >= MAX_PER_ROLE:
            continue
        if airport_counts[meta["airport"]] >= MAX_PER_AIRPORT:
            continue
        if accent_counts[meta["accent"]] >= MAX_PER_ACCENT:
            continue
        
        selected.append(ex)
        total_tokens += ex["tokens"]
        role_counts[meta["role"]] += 1
        airport_counts[meta["airport"]] += 1
        accent_counts[meta["accent"]] += 1
    
    print(f"\nSelected {len(selected)} examples")
    print(f"Estimated tokens: {total_tokens:,}")
    print(f"Estimated cost: ${total_tokens * 0.000008:.2f}")
    
    print("\n--- Distribution ---")
    print("\nBy Controller Role:")
    for role, count in sorted(role_counts.items(), key=lambda x: -x[1]):
        print(f"  {role}: {count}")
    
    print("\nBy Accent (top 10):")
    for accent, count in sorted(accent_counts.items(), key=lambda x: -x[1])[:10]:
        print(f"  {accent}: {count}")
    
    print("\nBy Airport (top 10):")
    for airport, count in sorted(airport_counts.items(), key=lambda x: -x[1])[:10]:
        print(f"  {airport}: {count}")
    
    print("\nScore distribution:")
    scores = [ex["score"] for ex in selected]
    print(f"  Min: {min(scores)}, Max: {max(scores)}, Avg: {sum(scores)/len(scores):.1f}")
    
    # Write output
    with open(OUTPUT_FILE, 'w', encoding='utf-8') as f:
        for ex in selected:
            f.write(json.dumps(ex["entry"]) + "\n")
    
    print(f"\nSaved to: {OUTPUT_FILE}")
    print("Ready for OpenAI fine-tuning upload!")

if __name__ == "__main__":
    main()

