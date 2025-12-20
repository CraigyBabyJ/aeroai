from __future__ import annotations

import os
import sys
import shutil
from pathlib import Path
from typing import Dict


def ensure_espeak_backend(base_dir: Path) -> Dict[str, object]:
    """
    On Windows, locate espeak-ng/espeak. If bundled, prepend its directory to PATH.
    Returns: {"found": bool, "source": "path"|"bundled"|None, "exe": str|None}
    """
    status: Dict[str, object] = {"found": False, "source": None, "exe": None}

    if sys.platform != "win32":
        status["found"] = True
        return status

    exe = shutil.which("espeak-ng") or shutil.which("espeak")
    if exe:
        status.update({"found": True, "source": "path", "exe": exe})
        return status

    candidates = [
        base_dir / "tools" / "espeak-ng" / "espeak-ng.exe",
        base_dir / "tools" / "espeak" / "espeak.exe",
    ]
    for cand in candidates:
        if cand.exists():
            bin_dir = str(cand.parent.resolve())
            current_path = os.environ.get("PATH") or ""
            if bin_dir not in current_path.split(os.pathsep):
                os.environ["PATH"] = os.pathsep.join([bin_dir, current_path]) if current_path else bin_dir
            status.update({"found": True, "source": "bundled", "exe": str(cand)})
            return status

    return status
