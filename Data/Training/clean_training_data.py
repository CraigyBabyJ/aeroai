"""
AeroAI Training Data Cleaner
Removes duplicates and low-quality entries from training JSONL file.
"""

import json
import os
from collections import Counter

# Get the directory where this script is located
SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
INPUT_FILE = os.path.join(SCRIPT_DIR, "training_aeroai.jsonl")
OUTPUT_FILE = os.path.join(SCRIPT_DIR, "training_aeroai_cleaned.jsonl")


def load_examples(filepath):
    """Load all examples from JSONL file."""
    examples = []
    with open(filepath, "r", encoding="utf-8") as f:
        for line_num, line in enumerate(f, 1):
            line = line.strip()
            if not line:
                continue
            try:
                obj = json.loads(line)
                examples.append((line_num, obj, line))
            except json.JSONDecodeError as e:
                print(f"[WARN] Line {line_num}: Invalid JSON - {e}")
    return examples


def get_signature(obj):
    """Create a signature for duplicate detection."""
    try:
        messages = obj.get("messages", [])
        # Use user content + assistant content as signature
        user_content = ""
        assistant_content = ""
        for msg in messages:
            if msg.get("role") == "user":
                user_content = msg.get("content", "")
            elif msg.get("role") == "assistant":
                assistant_content = msg.get("content", "")
        return (user_content.strip().lower(), assistant_content.strip().lower())
    except:
        return None


def is_low_quality(obj):
    """Check if example is low quality."""
    try:
        messages = obj.get("messages", [])

        user_content = ""
        assistant_content = ""
        system_content = ""

        for msg in messages:
            role = msg.get("role", "")
            content = msg.get("content", "")
            if role == "user":
                user_content = content
            elif role == "assistant":
                assistant_content = content
            elif role == "system":
                system_content = content

        # Check for empty or very short responses
        if len(assistant_content.strip()) < 5:
            return True, "Assistant response too short"

        # Check for missing PILOT message
        if "PILOT:" not in user_content and "pilot:" not in user_content.lower():
            return True, "Missing PILOT message"

        # Check for missing context JSON
        if "{" not in user_content:
            return True, "Missing context JSON"

        # Check for wrong controller_role
        if (
            '"controller_role": "CLEARANCE"' in user_content
            or '"controller_role":"CLEARANCE"' in user_content
        ):
            return True, "Wrong controller_role (CLEARANCE instead of DELIVERY)"

        # Check for inconsistent phonetics (tree/fower/fife mixed with numbers)
        bad_phonetics = ["tree", "fower", "fife"]
        if any(word in assistant_content.lower() for word in bad_phonetics):
            # Check if it's mixed with regular numbers spoken
            if any(
                num in assistant_content.lower() for num in ["three", "four", "five"]
            ):
                return True, "Mixed phonetics style"

        # Check for raw runway numbers in ATC response (should be spoken)
        import re

        # Pattern like "runway 27R" instead of "runway two seven right"
        if re.search(r"runway \d{2}[LRC]?[,\.]", assistant_content):
            return True, "Raw runway number (should be spoken)"

        # Check for unfilled placeholders (templates that weren't populated)
        placeholder_pattern = r"\{[A-Z_]+\}"
        if re.search(placeholder_pattern, user_content) or re.search(
            placeholder_pattern, assistant_content
        ):
            return True, "Contains unfilled placeholders"

        # Check for very short/useless ATC responses
        useless_responses = ["roger.", "correct.", "affirm."]
        if (
            assistant_content.strip().lower() in useless_responses
            and len(user_content) < 100
        ):
            # Only flag if context is also minimal
            pass  # These are actually valid, keep them

        # Check for responses that are just the callsign (too minimal for learning)
        words = assistant_content.strip().split()
        if len(words) <= 3 and not any(
            kw in assistant_content.lower()
            for kw in [
                "cleared",
                "approved",
                "negative",
                "contact",
                "climb",
                "descend",
                "turn",
                "hold",
                "taxi",
                "runway",
                "squawk",
            ]
        ):
            return True, "Response too minimal (just callsign)"

        return False, None

    except Exception as e:
        return True, f"Parse error: {e}"


def main():
    print(f"Loading {INPUT_FILE}...")
    examples = load_examples(INPUT_FILE)
    print(f"Loaded {len(examples)} examples")

    # Track duplicates
    seen_signatures = {}
    duplicates = []
    low_quality = []
    kept = []

    for line_num, obj, raw_line in examples:
        # Check for low quality
        is_bad, reason = is_low_quality(obj)
        if is_bad:
            low_quality.append((line_num, reason, raw_line[:100]))
            continue

        # Check for duplicates
        sig = get_signature(obj)
        if sig is None:
            low_quality.append((line_num, "Could not parse", raw_line[:100]))
            continue

        if sig in seen_signatures:
            duplicates.append((line_num, seen_signatures[sig], raw_line[:100]))
            continue

        seen_signatures[sig] = line_num
        kept.append(raw_line)

    # Report
    print(f"\n{'='*60}")
    print(f"RESULTS:")
    print(f"{'='*60}")
    print(f"Total loaded:     {len(examples)}")
    print(f"Duplicates found: {len(duplicates)}")
    print(f"Low quality:      {len(low_quality)}")
    print(f"Kept:             {len(kept)}")
    print(f"{'='*60}")

    # Show sample duplicates
    if duplicates:
        print(f"\nSample duplicates (first 10):")
        for line_num, orig_line, preview in duplicates[:10]:
            print(f"  Line {line_num} duplicates line {orig_line}: {preview}...")

    # Show low quality reasons
    if low_quality:
        print(f"\nLow quality reasons breakdown:")
        reasons = Counter(reason for _, reason, _ in low_quality)
        for reason, count in reasons.most_common():
            print(f"  {reason}: {count}")

        print(f"\nSample low quality (first 10):")
        for line_num, reason, preview in low_quality[:10]:
            print(f"  Line {line_num} ({reason}): {preview}...")

    # Write cleaned file
    print(f"\nWriting cleaned data to {OUTPUT_FILE}...")
    with open(OUTPUT_FILE, "w", encoding="utf-8") as f:
        for line in kept:
            f.write(line + "\n")

    print(f"Done! Cleaned file has {len(kept)} examples.")

    # Calculate savings
    removed = len(examples) - len(kept)
    if removed > 0:
        pct = (removed / len(examples)) * 100
        print(f"\nRemoved {removed} entries ({pct:.1f}%)")
        tokens_saved = removed * 200  # estimate
        cost_saved = (tokens_saved * 3 / 1_000_000) * 3  # 3 epochs
        print(f"Estimated training cost savings: ~${cost_saved:.2f}")


if __name__ == "__main__":
    main()
