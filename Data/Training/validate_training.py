"""
Validate training_aeroai.json for JSONL format errors
"""

import json
import os

SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
INPUT_FILE = os.path.join(SCRIPT_DIR, "training_aeroai.json")

errors = []
valid_count = 0
empty_lines = []

with open(INPUT_FILE, "r", encoding="utf-8") as f:
    for line_num, line in enumerate(f, 1):
        line = line.strip()
        if not line:
            empty_lines.append(line_num)
            continue
        try:
            obj = json.loads(line)
            # Validate structure
            if "messages" not in obj:
                errors.append((line_num, "Missing 'messages' key"))
            else:
                messages = obj["messages"]
                if not isinstance(messages, list):
                    errors.append((line_num, "'messages' is not a list"))
                elif len(messages) < 2:
                    errors.append((line_num, "Less than 2 messages"))
                else:
                    for i, msg in enumerate(messages):
                        if "role" not in msg:
                            errors.append((line_num, f"Message {i} missing 'role'"))
                        if "content" not in msg:
                            errors.append((line_num, f"Message {i} missing 'content'"))
            valid_count += 1
        except json.JSONDecodeError as e:
            errors.append((line_num, f"JSON error: {e}"))

print(f"Validated {valid_count} lines")
print(
    f"Empty lines: {len(empty_lines)} at lines: {empty_lines[:10]}{'...' if len(empty_lines) > 10 else ''}"
)
print(f"Errors found: {len(errors)}")

if errors:
    print("\nFirst 20 errors:")
    for line_num, err in errors[:20]:
        print(f"  Line {line_num}: {err}")
