#!/usr/bin/env python
"""Turn one raw `capture_wellness.py --plan wellness` run into committed fixtures.

The wellness payloads carry the account id under four key names, a display name,
per-day UUIDs and device ids, at any depth. Every other value is a physiological
figure or a timestamp and is kept, the same decision the Activity corpus made
(spikes/strength-data-spike/findings.md, "What was scrubbed").

    python scrub_wellness.py dumps/20261008T103754Z ../../fixtures/garmin

Nothing is published unless every file is a kind named below and no value from
the athlete's social profile, nor any raw value a rule replaced, survives
anywhere in the staged output.
"""

from __future__ import annotations

import argparse
import json
import re
import shutil
import sys
import tempfile
from pathlib import Path
from typing import Any
from urllib.parse import quote

PLACEHOLDER_OWNER_ID = 1
PLACEHOLDER_DEVICE_ID = 1
PLACEHOLDER_DISPLAY_NAME = "00000000-0000-4000-8000-00000000000a"

PROFILE_NAME = "userprofile-service--socialProfile.json"
PUBLISHED = (
    re.compile(r"^wellness-service--dailySleepData__date-\d{4}-\d{2}-\d{2}\.json$"),
    re.compile(r"^usersummary-service--daily__date-\d{4}-\d{2}-\d{2}\.json$"),
    re.compile(r"^wellness-service--bodyBattery-reports-daily__date-\d{4}-\d{2}-\d{2}\.json$"),
    re.compile(r"^wellness-service--dailyStress__date-\d{4}-\d{2}-\d{2}\.json$"),
)
# The profile is the census of what must not leak, never a fixture. HRV answered 204
# with no body on every date, and the sync does not call it (ADR 0011); the run's
# index keeps that evidence.
NOT_PUBLISHED = (
    re.compile(rf"^{re.escape(PROFILE_NAME)}$"),
    re.compile(r"^hrv-service--hrv__date-\d{4}-\d{2}-\d{2}\.json$"),
)
INDEX_NAME = "index.json"
PUBLISHED_INDEX_NAME = "index-wellness.json"

OWNER_ID_KEYS = frozenset({"userProfilePK", "userProfilePk", "userProfileId", "userDailySummaryId"})
# Profile fields too short or too generic to search for without false matches.
MIN_SWEEPABLE_LENGTH = 6


class LeakError(RuntimeError):
    """Something that must not be published was about to be. Nothing was written."""


class Scrubber:
    def __init__(self) -> None:
        self.replaced: set[str] = set()
        self._uuids: dict[str, str] = {}

    def _uuid(self, raw: str) -> str:
        if raw not in self._uuids:
            self._uuids[raw] = f"00000000-0000-4000-9000-{len(self._uuids) + 1:012d}"
        return self._uuids[raw]

    def scrub(self, value: Any) -> Any:
        if isinstance(value, list):
            return [self.scrub(item) for item in value]
        if not isinstance(value, dict):
            return value
        scrubbed = {}
        for key, raw in value.items():
            # Null stays null and 0 stays 0, so no shape is invented that Garmin did not send.
            if raw is None or isinstance(raw, (dict, list)):
                scrubbed[key] = self.scrub(raw)
                continue
            if key in OWNER_ID_KEYS:
                scrubbed[key] = PLACEHOLDER_OWNER_ID
            elif key == "displayName":
                scrubbed[key] = PLACEHOLDER_DISPLAY_NAME
            elif key == "uuid":
                scrubbed[key] = self._uuid(raw)
            elif key == "deviceId":
                scrubbed[key] = raw if raw == 0 else PLACEHOLDER_DEVICE_ID
            else:
                scrubbed[key] = raw
                continue
            self.replaced.add(str(raw))
        return scrubbed


def profile_values(profile: dict[str, Any]) -> set[str]:
    values = set()
    for raw in profile.values():
        if isinstance(raw, bool) or raw is None or isinstance(raw, (dict, list)):
            continue
        text = str(raw)
        if len(text) >= MIN_SWEEPABLE_LENGTH:
            values.add(text)
    display_name = profile.get("displayName")
    if display_name:
        values.add(quote(display_name, safe=""))
    return values


def survives(value: str, text: str) -> bool:
    # A number must match whole, so an account id is not found inside a timestamp.
    pattern = rf"(?<!\d){re.escape(value)}(?!\d)" if value.isdigit() else re.escape(value)
    return re.search(pattern, text) is not None


def scrub_run(run_dir: Path, out_dir: Path) -> list[str]:
    names = sorted(path.name for path in run_dir.iterdir() if path.is_file())
    unknown = [name for name in names if name != INDEX_NAME
               and not any(rule.match(name) for rule in PUBLISHED + NOT_PUBLISHED)]
    if unknown:
        raise LeakError(f"No rule covers {unknown}; nothing published")
    if PROFILE_NAME not in names or INDEX_NAME not in names:
        raise LeakError("A run needs its social profile and index to check against; nothing published")
    profile = json.loads((run_dir / PROFILE_NAME).read_text(encoding="utf-8"))
    display_name = quote(profile["displayName"], safe="")
    scrubber = Scrubber()
    staged: dict[str, str] = {}
    for name in names:
        if any(rule.match(name) for rule in PUBLISHED):
            payload = json.loads((run_dir / name).read_text(encoding="utf-8"))
            staged[name] = json.dumps(scrubber.scrub(payload), separators=(",", ":"))
    index = json.loads((run_dir / INDEX_NAME).read_text(encoding="utf-8"))
    for entry in index:
        entry["endpoint"] = entry["endpoint"].replace(display_name, PLACEHOLDER_DISPLAY_NAME)
    staged[PUBLISHED_INDEX_NAME] = json.dumps(
        [entry for entry in index if entry["file"] != PROFILE_NAME], indent=2) + "\n"

    forbidden = profile_values(profile) | {value for value in scrubber.replaced
                                           if len(value) >= MIN_SWEEPABLE_LENGTH}
    leaks = sorted({name for name, text in staged.items()
                    for value in forbidden if survives(value, text)})
    if leaks:
        raise LeakError(f"Raw identifying values survive in {leaks}; nothing published")

    with tempfile.TemporaryDirectory() as scratch:
        for name, text in staged.items():
            Path(scratch, name).write_text(text, encoding="utf-8")
        out_dir.mkdir(parents=True, exist_ok=True)
        for name in staged:
            shutil.copyfile(Path(scratch, name), out_dir / name)
    return sorted(staged)


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("run_dir", type=Path)
    parser.add_argument("out_dir", type=Path)
    args = parser.parse_args(argv)
    try:
        published = scrub_run(args.run_dir, args.out_dir)
    except LeakError as error:
        print(error, file=sys.stderr)
        return 1
    print("\n".join(published))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
