"""Replacing a small file without ever leaving a torn one behind. A torn watermark
stops the next run, and a torn sign-in costs an interactive one."""

from __future__ import annotations

import os
import time
from pathlib import Path


def write_replacing(path: Path, content: str) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    # Named for this process, so two writers cannot overwrite each other's scratch
    # and replace the target with half of the other's content.
    scratch = path.with_suffix(f".{os.getpid()}.tmp")
    scratch.write_text(content, encoding="utf-8")
    # Windows briefly refuses the replace while a scanner holds a file just written.
    for attempt in range(5):
        try:
            os.replace(scratch, path)
            return
        except PermissionError:
            if attempt == 4:
                raise
            time.sleep(0.05 * (attempt + 1))
