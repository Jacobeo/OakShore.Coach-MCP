#!/usr/bin/env python
"""Turn one raw dump run into the committed fixture corpus.

`dump_garmin.py` writes what Garmin sent, identifiers and coordinates included,
to a gitignored directory. This reads one of those runs and writes the tracked
corpus under `fixtures/garmin/`, with the account, the device, the athlete's
name and anything locating a session replaced by stable placeholders.

Stable, not random: one account becomes one placeholder everywhere, so a fixture
that joins an Activity to its owner still joins, and re-running this over the
same run produces the same bytes rather than a diff.

Only the Activity list carries identifying fields. `exerciseSets`,
`hrTimeInZones` and `heartRateZones` carry none, and are copied unchanged - the
corpus is meant to be the bytes Garmin sent wherever that is safe.

    python scrub_dumps.py dumps/20260922T090450Z ../../fixtures/garmin

Nothing is published unless three checks pass, because a scrubber that publishes
on a rule it failed to apply is worse than no scrubber:

1. Every file is a kind the rules were written for. An unrecognised file is
   refused rather than copied - an `ABORT-*.txt` holds the request URL and
   Garmin's error body, and copying it needs no rule to be wrong to leak.
2. There is an Activity list to build the census from. Without one the census is
   empty and every later check passes vacuously.
3. No raw value survives - checked by value on the scrubbed entries, and by a
   text sweep of every staged file for values long enough to search for.

The reasoning behind each rule, and what was deliberately left alone, is in
`findings.md` beside this file.
"""

from __future__ import annotations

import argparse
import json
import re
import shutil
import sys
import tempfile
from pathlib import Path

PLACEHOLDER_OWNER_ID = 1
PLACEHOLDER_DEVICE_ID = 1
PLACEHOLDER_FULL_NAME = "Athlete"
PLACEHOLDER_DISPLAY_NAME = "00000000-0000-4000-8000-00000000000a"
PLACEHOLDER_PROFILE_IMAGE = "https://example.invalid/profile.png"
PLACEHOLDER_LOCATION_NAME = "Somewhere"
PLACEHOLDER_USER_ROLES = ["ROLE_CONNECTUSER"]

ACTIVITY_LIST_PREFIX = "activitylist-service--search-activities__page-"

ACTIVITY_LIST = re.compile(rf"^{re.escape(ACTIVITY_LIST_PREFIX)}\d+\.json$")

# Copied rather than scrubbed, because a key census of all three found only
# metric fields. Checked on every run rather than trusted: a file here that has
# grown a field the Activity list scrubs is refused rather than copied.
COPIED_VERBATIM = (
    re.compile(r"^activity-service--exerciseSets__activity-\d+\.json$"),
    re.compile(r"^activity-service--hrTimeInZones__activity-\d+\.json$"),
    re.compile(r"^biometric-service--heartRateZones\.json$"),
    re.compile(r"^index\.json$"),
    re.compile(r"^exercise-set-cases\.md$"),
)

# Below this, a raw value is too short to search a file for without matching
# unrelated text - `A` occurs inside `ACTIVE`. Short values are checked by value
# on the scrubbed entry instead, which is exact.
MIN_SWEEPABLE_LENGTH = 6


class LeakError(RuntimeError):
    """Something that must not be published was about to be. Nothing was written."""


def _constant(value):
    return lambda raw, entry: value


def _placeholder_when_present(value):
    return lambda raw, entry: None if raw is None else value


def _device_id(raw, entry):
    # 0 is not a device, it is the absence of one, and losing that loses a shape.
    return raw if raw in (None, 0) else PLACEHOLDER_DEVICE_ID


def _activity_name(raw, entry):
    # Free text the athlete typed. This athlete's names state an injury and name
    # opposing football clubs; a fixture needs a string, not that string.
    if raw is None:
        return None
    return (entry.get("activityType") or {}).get("typeKey") or "activity"


def _activity_uuid(raw, entry):
    # Falsy means Garmin sent no UUID; there is nothing to pseudonymise and
    # nothing in the mapping to look up.
    return raw if not raw else entry["_uuidPlaceholders"][raw]


def _user_roles(raw, entry):
    return None if raw is None else list(PLACEHOLDER_USER_ROLES)


# Field -> how it is replaced. The key set is also the census of what must never
# reach the repository, and this mapping is the authority on what each field
# should end up holding - see `_assert_every_field_was_replaced`.
SCRUBBED_FIELDS = {
    "activityName": _activity_name,
    "activityUUID": _activity_uuid,
    "deviceId": _device_id,
    "endLatitude": _constant(None),
    "endLongitude": _constant(None),
    "locationName": _placeholder_when_present(PLACEHOLDER_LOCATION_NAME),
    "ownerDisplayName": _constant(PLACEHOLDER_DISPLAY_NAME),
    "ownerFullName": _constant(PLACEHOLDER_FULL_NAME),
    "ownerId": _constant(PLACEHOLDER_OWNER_ID),
    "ownerProfileImageUrlLarge": _placeholder_when_present(PLACEHOLDER_PROFILE_IMAGE),
    "ownerProfileImageUrlMedium": _placeholder_when_present(PLACEHOLDER_PROFILE_IMAGE),
    "ownerProfileImageUrlSmall": _placeholder_when_present(PLACEHOLDER_PROFILE_IMAGE),
    "startLatitude": _constant(None),
    "startLongitude": _constant(None),
    "userRoles": _user_roles,
}


def _uuid_placeholder(index):
    return f"00000000-0000-4000-8000-{index:012d}"


def _uuid_mapping(pages):
    raw = sorted(
        {
            entry["activityUUID"]
            for entries in pages.values()
            for entry in entries
            if entry.get("activityUUID")
        }
    )
    return {value: _uuid_placeholder(i) for i, value in enumerate(raw)}


def _scrub_entry(entry, rules, uuid_placeholders):
    context = dict(entry)
    context["_uuidPlaceholders"] = uuid_placeholders
    scrubbed = dict(entry)
    for field, rule in rules.items():
        if field in scrubbed:
            scrubbed[field] = rule(entry[field], context)
    return scrubbed


def _assert_every_field_was_replaced(pages, scrubbed_pages, uuid_placeholders):
    """Check each scrubbed field against what the census says it should hold.

    Exact, and independent of how short or how common the value is, so this is
    what catches a rule that is missing or that quietly returned its input. The
    authority is `SCRUBBED_FIELDS` rather than the caller's `rules`: dropping a
    rule has to be a refusal, not an exemption.

    Comparing against the census rather than against the raw value is also what
    keeps the tool idempotent. On a second pass the raw value *is* the
    placeholder, which is indistinguishable from an unapplied rule if the only
    question asked is "did this change".
    """
    for name, entries in pages.items():
        for raw_entry, scrubbed_entry in zip(entries, scrubbed_pages[name]):
            context = dict(raw_entry)
            context["_uuidPlaceholders"] = uuid_placeholders
            for field, rule in SCRUBBED_FIELDS.items():
                if field not in raw_entry:
                    continue
                if scrubbed_entry.get(field) != rule(raw_entry[field], context):
                    raise LeakError(
                        f"{name} still carries its raw {field}; nothing was published"
                    )


def _sweepable_literals(pages):
    """Raw values long enough to search a file for without false positives."""
    literals = {}
    for entries in pages.values():
        for entry in entries:
            for field in SCRUBBED_FIELDS:
                raw = entry.get(field)
                values = raw if isinstance(raw, list) else [raw]
                for value in values:
                    if value is None or isinstance(value, bool):
                        continue
                    for rendered in (str(value), json.dumps(value).strip('"')):
                        if len(rendered) >= MIN_SWEEPABLE_LENGTH:
                            literals.setdefault(rendered, field)
    return literals


def _assert_nothing_swept_up(staged, literals, emitted):
    """Sweep every staged file for any raw value that is not a placeholder."""
    for path in sorted(staged.iterdir()):
        text = path.read_text(encoding="utf-8")
        for literal, field in literals.items():
            if literal not in emitted and literal in text:
                raise LeakError(
                    f"{path.name} carries a raw {field}; nothing was published"
                )


def _assert_copied_files_carry_no_identifying_key(path, payload):
    """The copied payloads are copied because their keys are all metric fields.

    That is a fact about a payload shape Garmin can change, so it is re-checked
    on every run rather than trusted from the census that established it once.
    """
    keys = set()
    stack = [payload]
    while stack:
        node = stack.pop()
        if isinstance(node, dict):
            keys |= set(node)
            stack += list(node.values())
        elif isinstance(node, list):
            stack += node
    grown = sorted(keys & set(SCRUBBED_FIELDS))
    if grown:
        raise LeakError(
            f"{path.name} has grown {', '.join(grown)}, which is scrubbed on the "
            "Activity list and would be published unscrubbed here; "
            "nothing was published"
        )


def _classify(path):
    if ACTIVITY_LIST.match(path.name):
        return "list"
    if any(pattern.match(path.name) for pattern in COPIED_VERBATIM):
        return "copy"
    return None


def scrub_run(run, out, rules=None):
    """Write a scrubbed copy of `run` to `out` and return the names written.

    Raises `LeakError` and writes nothing if any of the three checks fails.
    """
    run = Path(run)
    out = Path(out)
    rules = SCRUBBED_FIELDS if rules is None else rules

    files = [path for path in sorted(run.iterdir()) if path.is_file()]
    kinds = {path: _classify(path) for path in files}

    unknown = [path.name for path, kind in kinds.items() if kind is None]
    if unknown:
        raise LeakError(
            f"no rule covers {', '.join(unknown)}, and copying a file the rules "
            "were not written for is how a leak gets in without a rule being "
            "wrong; nothing was published"
        )

    pages = {
        path.name: json.loads(path.read_text(encoding="utf-8"))
        for path, kind in kinds.items()
        if kind == "list"
    }
    if not pages:
        raise LeakError(
            "no Activity list in this run, so there is nothing to build the "
            "census from and every later check would pass vacuously; "
            "nothing was published"
        )

    uuid_placeholders = _uuid_mapping(pages)
    scrubbed_pages = {
        name: [_scrub_entry(entry, rules, uuid_placeholders) for entry in entries]
        for name, entries in pages.items()
    }

    staging = Path(tempfile.mkdtemp(prefix="scrub-", dir=run.parent))
    try:
        for path, kind in kinds.items():
            if kind == "list":
                (staging / path.name).write_text(
                    json.dumps(scrubbed_pages[path.name], indent=1) + "\n",
                    encoding="utf-8",
                )
                continue
            if path.suffix == ".json":
                _assert_copied_files_carry_no_identifying_key(
                    path, json.loads(path.read_text(encoding="utf-8"))
                )
            shutil.copy2(path, staging / path.name)

        _assert_every_field_was_replaced(pages, scrubbed_pages, uuid_placeholders)
        _assert_nothing_swept_up(
            staging,
            _sweepable_literals(pages),
            _sweepable_literals(scrubbed_pages),
        )

        out.mkdir(parents=True, exist_ok=True)
        written = []
        for path in sorted(staging.iterdir()):
            shutil.copy2(path, out / path.name)
            written.append(path.name)
    finally:
        shutil.rmtree(staging, ignore_errors=True)

    return written


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("run", type=Path, help="one dumps/<run-timestamp> directory")
    parser.add_argument("out", type=Path, help="where the corpus is written")
    args = parser.parse_args(argv)

    if not args.run.is_dir():
        print(f"no such run: {args.run}", file=sys.stderr)
        return 2

    written = scrub_run(args.run, args.out)
    print(f"scrubbed {args.run} -> {args.out}")
    for name in written:
        print(f"  {name}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
