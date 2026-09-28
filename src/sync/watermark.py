"""The sync's local record of which days it has completed."""

from __future__ import annotations

import json
import os
from datetime import date, timedelta
from pathlib import Path


class Watermark:
    def __init__(self, path: Path) -> None:
        self._path = path
        self._completed = self._load()

    def _load(self) -> list[tuple[date, date]]:
        if not self._path.is_file():
            return []
        try:
            recorded = json.loads(self._path.read_text(encoding="utf-8"))
            return [(date.fromisoformat(item["from"]), date.fromisoformat(item["to"]))
                    for item in recorded["completedDays"]]
        except (ValueError, KeyError, TypeError) as error:
            raise RuntimeError(
                f"Watermark file {self._path} is unreadable; fix or remove it") from error

    def _save(self) -> None:
        self._path.parent.mkdir(parents=True, exist_ok=True)
        content = json.dumps({"completedDays": [
            {"from": start.isoformat(), "to": end.isoformat()} for start, end in self._completed
        ]})
        # Write-then-replace so an interrupted run never tears the file.
        scratch = self._path.with_suffix(".tmp")
        scratch.write_text(content, encoding="utf-8")
        os.replace(scratch, self._path)

    def pending_days(self, earliest: date, today: date) -> list[date]:
        """Days in [earliest, today] not yet completed, most recent first."""
        days = []
        day = today
        while day >= earliest:
            if not any(start <= day <= end for start, end in self._completed):
                days.append(day)
            day -= timedelta(days=1)
        return days

    def mark_completed(self, day: date) -> None:
        merged: list[tuple[date, date]] = []
        start, end = day, day
        for existing_start, existing_end in self._completed:
            if existing_start <= day <= existing_end:
                return
            if existing_end + timedelta(days=1) == start:
                start = existing_start
            elif end + timedelta(days=1) == existing_start:
                end = existing_end
            else:
                merged.append((existing_start, existing_end))
        merged.append((start, end))
        merged.sort()
        self._completed = merged
        self._save()
