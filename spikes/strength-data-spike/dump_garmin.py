#!/usr/bin/env python3
"""Throwaway harness for the strength data spike (.scratch/01-strength-data-spike.md).

Logs in to Garmin Connect once by hand, persists the token, then dumps the raw
bytes of an Activity list over a recent window, of the `exerciseSets` response
for every strength Activity in it, of the `hrTimeInZones` response for every
other Activity, and of the configured zone boundaries once. Run it from the
home machine only: Garmin rate-limits by IP and datacenter ranges fare worse
(ADR 0001).

This is not the sync. It is not structured for reuse and must not be grown into
spec 03; its durable output is the files under dumps/.

    python dump_garmin.py --days 14
"""

from __future__ import annotations

import argparse
import json
import os
import sys
import time
from collections.abc import Callable, Iterator
from datetime import UTC, date, datetime, timedelta
from getpass import getpass
from pathlib import Path
from typing import Any

# A 401 means the token is dead, a 403 means Garmin is refusing us, and a 429
# means we are already over a limit that retrying would extend (ADR 0005).
# Every other error status stops the run too; these three are called out
# because they are the ones a retry would actively make worse.
ABORT_STATUSES = (401, 403, 429)

ACTIVITY_LIST_PATH = "/activitylist-service/activities/search/activities"
ACTIVITY_LIST_NAME = "activitylist-service--search-activities"
# These four are every path the spike can ask for. The per-Activity summary
# endpoint is deliberately not among them (ADR 0007).
EXERCISE_SETS_PATH = "/activity-service/activity/{activity_id}/exerciseSets"
EXERCISE_SETS_NAME = "activity-service--exerciseSets"
HR_TIME_IN_ZONES_PATH = "/activity-service/activity/{activity_id}/hrTimeInZones"
HR_TIME_IN_ZONES_NAME = "activity-service--hrTimeInZones"
# Per athlete and per sport profile, not per Activity, so it is fetched once.
HEART_RATE_ZONES_PATH = "/biometric-service/heartRateZones"
HEART_RATE_ZONES_NAME = "biometric-service--heartRateZones"
FIELD_CHECK_NAME = "activity-list-field-check.md"
COVERAGE_NAME = "exercise-set-coverage.md"
ZONE_COVERAGE_NAME = "heart-rate-zone-coverage.md"
# Beside the dumps rather than beside the README: it names Activities, so it is
# gitignored with them. Activity identifiers turned out to be the one class the
# scrubbing decision keeps, so a copy reaches the committed corpus.
CASES_NAME = "exercise-set-cases.md"
PAGE_SIZE = 20
# A few weeks of Activities is far under this. The cap exists so a server that
# never returns an empty page cannot loop us into a burst of requests.
MAX_PAGES = 50

STRENGTH_TOTALS = ("totalSets", "activeSets", "totalReps")
# The totals decide whether spec 03 can skip a per-Activity call; the summary
# array turned out to be on the list entry too, so it is counted here rather
# than rediscovered from a second fetch.
LIST_FIELDS_IN_QUESTION = (*STRENGTH_TOTALS, "summarizedExerciseSets")


class AbortSignal(Exception):
    """Garmin returned something the spike must stop on, without retrying."""

    def __init__(
        self,
        status_code: int,
        url: str,
        params: dict[str, str] | None,
        body: bytes,
        retry_after: str | None,
    ) -> None:
        super().__init__(f"HTTP {status_code} from {url}")
        self.status_code = status_code
        self.url = url
        self.params = params
        self.body = body
        self.retry_after = retry_after

    @property
    def retrying_would_extend_the_block(self) -> bool:
        return self.status_code in ABORT_STATUSES

    def report(self) -> str:
        verdict = (
            "an auth or rate-limit refusal; retrying extends the block (ADR 0005)"
            if self.retrying_would_extend_the_block
            else "an unexpected error; the spike does not retry anything"
        )
        return "\n".join(
            [
                f"status:      {self.status_code} - {verdict}",
                f"url:         {self.url}",
                f"params:      {self.params}",
                f"Retry-After: {self.retry_after or '(not sent)'}",
                "body (first 2000 bytes):",
                self.body[:2000].decode("utf-8", "replace"),
            ]
        )


class PacedReader:
    """GET-only, paced, and stops the run on any error status.

    GET-only is the enforcement of "the spike makes no Garmin write of any
    kind": there is no code path here that can issue anything else.

    It reads from Garmin's authenticated session rather than through
    ``Garmin.connectapi``; see README.md, "Reading the bytes without a parser".
    """

    DEFAULT_PACE_SECONDS = 4.0

    def __init__(
        self,
        session: Any,
        base_url: str,
        headers: Callable[[], dict[str, str]],
        pace_seconds: float = DEFAULT_PACE_SECONDS,
        sleep: Callable[[float], None] = time.sleep,
        already_called: bool = False,
    ) -> None:
        self._session = session
        self._base_url = base_url.rstrip("/")
        self._headers = headers
        self._pace_seconds = pace_seconds
        self._sleep = sleep
        # Login has already talked to Garmin, so the first read is paced behind it.
        self._called = already_called
        self._abort: AbortSignal | None = None
        self.call_count = 0

    def get(self, path: str, params: dict[str, str] | None = None) -> bytes:
        if self._abort is not None:
            raise self._abort
        if self._called:
            self._sleep(self._pace_seconds)
        self._called = True

        url = f"{self._base_url}/{path.lstrip('/')}"
        response = self._session.request(
            "GET", url, headers=self._headers(), params=params, timeout=30
        )
        self.call_count += 1

        if response.status_code >= 400:
            self._abort = AbortSignal(
                response.status_code,
                url,
                params,
                response.content,
                response.headers.get("Retry-After"),
            )
            raise self._abort
        return response.content


class DumpWriter:
    """Writes response bytes verbatim, and an index saying where each came from."""

    def __init__(self, out_dir: Path) -> None:
        self.out_dir = out_dir
        self.out_dir.mkdir(parents=True, exist_ok=True)
        self._index_path = out_dir / "index.json"
        self._entries: dict[str, dict[str, Any]] = {}
        if self._index_path.exists():
            for entry in json.loads(self._index_path.read_text("utf-8")):
                self._entries[entry["file"]] = entry

    @staticmethod
    def file_name(
        endpoint_name: str,
        *,
        activity_id: Any = None,
        page: int | None = None,
    ) -> str:
        name = endpoint_name
        if activity_id is not None:
            name += f"__activity-{activity_id}"
        if page is not None:
            name += f"__page-{page:02d}"
        return f"{name}.json"

    def write(
        self,
        endpoint_name: str,
        body: bytes,
        *,
        path: str,
        params: dict[str, str] | None = None,
        activity_id: str | None = None,
        page: int | None = None,
    ) -> Path:
        target = self.out_dir / self.file_name(
            endpoint_name, activity_id=activity_id, page=page
        )
        target.write_bytes(body)

        self._entries[target.name] = {
            "file": target.name,
            "endpoint": path,
            "params": params,
            "activityId": activity_id,
            "bytes": len(body),
            "fetchedAtUtc": datetime.now(UTC).isoformat(timespec="seconds"),
        }
        ordered = sorted(self._entries.values(), key=lambda entry: entry["file"])
        self._index_path.write_text(json.dumps(ordered, indent=2) + "\n", "utf-8")
        return target

    def write_note(self, filename: str, text: str) -> Path:
        target = self.out_dir / filename
        target.write_text(text, "utf-8")
        return target


def resolve_token_dir(override: str | None) -> Path:
    """See docs/adr/0006-garmin-token-file-location.md for why this default."""
    return Path(override or os.environ.get("GARMINTOKENS") or "~/.garminconnect")


def authenticate(token_dir: Path) -> Any:
    """Reuse the stored token if there is one; otherwise log in by hand, once."""
    from garminconnect import Garmin
    from garminconnect.client import token_file_path

    token_file = token_file_path(str(token_dir))
    if token_file.exists():
        print(f"Reusing token: {token_file}")
        client = Garmin()
        client.login(tokenstore=str(token_dir))
        print("Logged in from the stored token - no credentials, no MFA prompt.")
        return client

    print(f"No token at {token_file} - interactive login required.")
    email = input("Garmin email: ").strip()
    password = getpass("Garmin password (not stored, not echoed): ")
    client = Garmin(
        email=email,
        password=password,
        prompt_mfa=lambda: input("MFA code from Garmin: ").strip(),
    )
    client.login(tokenstore=str(token_dir))
    # python-garminconnect swallows a failed token write, which would cost the
    # athlete a second interactive MFA login to discover. Check it landed.
    if not token_file.exists():
        raise RuntimeError(
            f"Login succeeded but no token was written to {token_file}. "
            "Fix the path or its permissions before running again - every run "
            "until then needs another MFA code."
        )
    print(f"Logged in. Token written to {token_file}")
    return client


def reader_for(client: Any, pace_seconds: float) -> PacedReader:
    # These three are private to the library; see README.md, "Reading the bytes
    # without a parser".
    return PacedReader(
        session=client.client._api_session,
        base_url=client.client._connectapi,
        headers=client.client.get_api_headers,
        pace_seconds=pace_seconds,
        already_called=True,
    )


def fetch_activity_list(
    reader: PacedReader,
    writer: DumpWriter,
    start: date,
    end: date,
) -> Iterator[tuple[Path, list[dict[str, Any]]]]:
    """One paced pass over the window, 20 Activities to a page.

    Every response is written verbatim before anything looks at it, so a
    surprising payload is on disk rather than lost to the parse that choked on
    it. The bytes are then parsed in memory only to decide whether another page
    exists - nothing parsed is ever written.
    """
    for page in range(MAX_PAGES):
        params = {
            "startDate": start.isoformat(),
            "endDate": end.isoformat(),
            "start": str(page * PAGE_SIZE),
            "limit": str(PAGE_SIZE),
        }
        body = reader.get(ACTIVITY_LIST_PATH, params=params)
        dumped = writer.write(
            ACTIVITY_LIST_NAME,
            body,
            path=ACTIVITY_LIST_PATH,
            params=params,
            page=page,
        )

        try:
            entries = json.loads(body) if body.strip() else []
        except json.JSONDecodeError as error:
            raise RuntimeError(
                f"{ACTIVITY_LIST_PATH} returned something that is not JSON. "
                f"The bytes are in {dumped}; read them. ({error})"
            ) from error
        if not isinstance(entries, list):
            raise RuntimeError(
                f"Expected a JSON array from {ACTIVITY_LIST_PATH}, got "
                f"{type(entries).__name__}. The bytes are in {dumped}; read them."
            )

        print(f"  page {page}: {len(entries)} Activities -> {dumped.name}")
        if not entries:
            return
        yield dumped, entries
        if len(entries) < PAGE_SIZE:
            return
    raise RuntimeError(f"Activity list did not end within {MAX_PAGES} pages; stopping.")


def type_key(activity: dict[str, Any]) -> str:
    return (activity.get("activityType") or {}).get("typeKey") or ""


def is_strength(activity: dict[str, Any]) -> bool:
    if "strength" in type_key(activity).lower():
        return True
    # A non-zero strength total on an unexpected typeKey is still a strength
    # Activity; a zeroed one on a run is not.
    return any(activity.get(field) for field in STRENGTH_TOTALS)


def fetch_exercise_sets(
    reader: PacedReader,
    writer: DumpWriter,
    activities: list[dict[str, Any]],
) -> None:
    """One paced call per strength Activity, written verbatim before it is read.

    The read endpoint shares its path with the write that ADR 0004 forbids,
    and `PacedReader` is what keeps them apart: it can only issue `GET`.
    """
    for activity in activities:
        activity_id = str(activity["activityId"])
        path = EXERCISE_SETS_PATH.format(activity_id=activity_id)
        body = reader.get(path)
        target = writer.write(
            EXERCISE_SETS_NAME, body, path=path, activity_id=activity_id
        )
        print(f"  Activity {activity_id} -> {target.name} ({len(body)} bytes)")


def fetch_hr_time_in_zones(
    reader: PacedReader,
    writer: DumpWriter,
    activities: list[dict[str, Any]],
) -> None:
    """One paced call per cardio Activity, written verbatim before it is read."""
    for activity in activities:
        activity_id = str(activity["activityId"])
        path = HR_TIME_IN_ZONES_PATH.format(activity_id=activity_id)
        body = reader.get(path)
        target = writer.write(
            HR_TIME_IN_ZONES_NAME, body, path=path, activity_id=activity_id
        )
        print(f"  Activity {activity_id} -> {target.name} ({len(body)} bytes)")


def fetch_heart_rate_zones(reader: PacedReader, writer: DumpWriter) -> None:
    """The configured zone boundaries, for every sport profile, in one call.

    Fetched before the per-Activity passes so that an abort part way through
    them still leaves the boundaries on disk: without them a `hrTimeInZones`
    dump is a row of seconds against zone numbers that mean nothing.
    """
    body = reader.get(HEART_RATE_ZONES_PATH)
    target = writer.write(HEART_RATE_ZONES_NAME, body, path=HEART_RATE_ZONES_PATH)
    print(f"  zone boundaries -> {target.name} ({len(body)} bytes)")


def followed_a_workout(activity: dict[str, Any]) -> bool:
    """Whether `workoutId` links this Activity back to a Workout.

    A freestyle Activity has none, and the two cases are expected to produce
    different `exerciseSets` payloads (ADR 0007).
    """
    return activity.get("workoutId") is not None


def dump_for(run_dir: Path, endpoint_name: str, activity_id: Any = None) -> Path | None:
    target = run_dir / DumpWriter.file_name(endpoint_name, activity_id=activity_id)
    return target if target.exists() else None


def exercise_set_count(dump: Path) -> int | None:
    """How many `exerciseSets` rows a dump holds; None if they cannot be read.

    An empty array is worth telling apart from a dump that was never fetched,
    so the count is reported. Nothing here may raise: the bytes cost a paced
    call and are already on disk, and a note that crashes on them would send
    the athlete back to Garmin for a payload they already have.
    """
    try:
        payload = json.loads(dump.read_text("utf-8", "replace") or "null")
    except json.JSONDecodeError:
        return None
    if not isinstance(payload, dict) or not isinstance(
        payload.get("exerciseSets"), list
    ):
        return None
    return len(payload["exerciseSets"])


def json_array_in(dump: Path | None) -> list[dict[str, Any]] | None:
    """The JSON array of objects in a dump; None if it is absent or not one.

    Nothing here may raise: the bytes cost a paced call and are already on
    disk, and a note that crashes on them would send the athlete back to
    Garmin for a payload they already have.
    """
    if dump is None:
        return None
    try:
        payload = json.loads(dump.read_text("utf-8", "replace") or "null")
    except json.JSONDecodeError:
        return None
    if not isinstance(payload, list) or not all(
        isinstance(row, dict) for row in payload
    ):
        return None
    return payload


def zone_floors(rows: list[dict[str, Any]] | None) -> tuple[Any, ...] | None:
    """The `zoneLowBoundary` of each zone row, in the order Garmin sent them."""
    if rows is None:
        return None
    return tuple(row.get("zoneLowBoundary") for row in rows)


def zone_attribution(
    profiles: list[dict[str, Any]], floors: tuple[Any, ...] | None
) -> str:
    """Which configured profile an Activity's zone boundaries came from.

    Matched on the boundaries, because the `hrTimeInZones` response names no
    profile. Two profiles can be configured with identical floors, and an
    Activity then genuinely cannot be attributed to one of them - saying so
    beats naming whichever happened to be last.
    """
    if not floors or any(floor is None for floor in floors):
        return "unreadable"
    matched = [
        profile.get("sport")
        for profile in profiles
        if profile_floors(profile) == floors
    ]
    if not matched:
        return "no configured profile"
    if len(matched) > 1:
        return " or ".join(f"`{sport}`" for sport in matched) + " (identical floors)"
    return f"`{matched[0]}`"


def profile_floors(profile: dict[str, Any]) -> tuple[Any, ...]:
    """The same boundaries as the configured profile states them.

    The two endpoints disagree on shape - a row per zone against a
    `zoneNFloor` key per zone - so one is put in the other's terms to compare
    them at all.
    """
    numbered = {
        int(key[len("zone") : -len("Floor")]): value
        for key, value in profile.items()
        if key.startswith("zone")
        and key.endswith("Floor")
        and key[len("zone") : -len("Floor")].isdigit()
    }
    return tuple(numbered[zone] for zone in sorted(numbered))


def note_header(title: str, summary: str) -> list[str]:
    generated = datetime.now(UTC).isoformat(timespec="seconds")
    return [
        f"# {title}",
        "",
        f"Generated by `dump_garmin.py` at {generated}.",
        summary,
        "",
    ]


def build_exercise_set_cases(strength: list[dict[str, Any]]) -> str:
    """Which dump came from which case, by Activity, for the run directory.

    The tracked coverage note carries no identifiers, so this is where the two
    cases are tied to individual dumps.
    """
    lines = [
        "# Which case each ExerciseSet dump came from",
        "",
        "| Activity | followed a Workout | dump |",
        "| --- | --- | --- |",
    ]
    for act in strength:
        activity_id = act.get("activityId")
        lines.append(
            f"| {activity_id} "
            f"| {'yes' if followed_a_workout(act) else 'no'} "
            f"| `{DumpWriter.file_name(EXERCISE_SETS_NAME, activity_id=activity_id)}` |"
        )
    return "\n".join(lines) + "\n"


def build_exercise_set_coverage(
    strength: list[dict[str, Any]],
    run_dir: Path,
    start: date,
    end: date,
) -> str:
    """Record which strength Activities were dumped, and from which case.

    A window holding only one of the two cases says so, rather than leaving it
    to be inferred from an absence.
    """
    followed = [act for act in strength if followed_a_workout(act)]
    freestyle = [act for act in strength if not followed_a_workout(act)]
    located = [
        (act, dump_for(run_dir, EXERCISE_SETS_NAME, act.get("activityId")))
        for act in strength
    ]
    on_disk = [act for act, dump in located if dump is not None]

    lines = note_header(
        "ExerciseSet dump coverage",
        (
            f"Window {start.isoformat()} to {end.isoformat()}: "
            f"{len(strength)} strength Activities, {len(on_disk)} with their "
            f"`exerciseSets` response on disk."
        ),
    ) + [
        "Read off the raw dumps under `dumps/`, not off Garmin's documentation.",
        "Activity and Workout identifiers are deliberately absent here: they are",
        "in the dumps, which are not committed, and in `exercise-set-cases.md`",
        "beside them. What the rows hold - `weight`, Exercise names, `setType` -",
        "is answered in `findings.md`; this note records only what was fetched",
        "and which of the two cases it came from.",
        "",
        "## The two cases",
        "",
        f"- Followed a Workout pushed to the watch: **{len(followed)}**",
        f"- Freestyle, no Workout linked: **{len(freestyle)}**",
        "",
    ]
    if strength and not freestyle:
        lines += [
            "No freestyle Activity in this window, which is the athlete's normal",
            "case rather than a gap: every strength Activity is lifted against a",
            "Workout pushed to the watch. The degraded case is unobserved, and",
            "nothing downstream should be designed for it.",
            "",
        ]
    elif strength and not followed:
        lines += [
            "**No followed-Workout strength Activity exists in this window**,",
            "which would be a surprise: the athlete lifts against a Workout",
            "pushed to the watch. Check the window before reading the payloads.",
            "",
        ]
    elif not strength:
        lines += [
            "**No strength Activity exists in this window**, so neither case is",
            "dumped. Widen `--days` before reading anything into that.",
            "",
        ]

    lines += [
        "## Per Activity",
        "",
        "| # | typeKey | followed a Workout | `exerciseSets` rows | dump |",
        "| --- | --- | --- | --- | --- |",
    ]
    for row, (act, dump) in enumerate(located, start=1):
        if dump is None:
            rows, state = "-", "missing"
        else:
            count = exercise_set_count(dump)
            rows = "unreadable" if count is None else str(count)
            state = "on disk"
        lines.append(
            f"| {row} | {type_key(act)} "
            f"| {'yes' if followed_a_workout(act) else 'no'} | {rows} | {state} |"
        )

    missing = len(strength) - len(on_disk)
    if missing:
        lines += [
            "",
            (
                f"**{missing} of them have no dump in this run directory.** "
                "Either the pass has not been run live over this window, or it "
                "stopped early - an `ABORT-*.txt` beside the dumps says which."
            ),
        ]

    return "\n".join(lines) + "\n"


def build_heart_rate_zone_coverage(
    cardio: list[dict[str, Any]],
    run_dir: Path,
    start: date,
    end: date,
) -> str:
    """Record which cardio Activities were dumped, and against which zones.

    The boundaries are per sport profile, and a cross-sport comparison built
    on one wrong set would be silently wrong, so which profile each Activity
    was scored against is checked rather than assumed.
    """
    located = [
        (act, dump_for(run_dir, HR_TIME_IN_ZONES_NAME, act.get("activityId")))
        for act in cardio
    ]
    on_disk = [act for act, dump in located if dump is not None]
    by_type: dict[str, int] = {}
    for act in cardio:
        by_type[type_key(act)] = by_type.get(type_key(act), 0) + 1

    lines = note_header(
        "Heart-rate zone coverage",
        (
            f"Window {start.isoformat()} to {end.isoformat()}: "
            f"{len(cardio)} cardio Activities, {len(on_disk)} with their "
            f"`hrTimeInZones` response on disk."
        ),
    ) + [
        "Read off the raw dumps under `dumps/`, not off Garmin's documentation.",
        "Activity identifiers and the boundaries in bpm are deliberately absent",
        "here: they are in the dumps, which are not committed. What this note",
        "records is what was fetched, and which zones each Activity was scored",
        "against.",
        "",
        "## Sports in the window",
        "",
    ]
    for key, count in sorted(by_type.items()):
        lines.append(f"- `{key or '(none)'}`: {count}")
    lines.append("")

    ran = any("running" in key for key in by_type)
    biked = any("cycling" in key or "biking" in key for key in by_type)
    if ran and biked:
        lines += [
            "A run and a bike are both here, so the cardio path is validated for",
            "both rather than for one sport and assumed for the other.",
            "",
        ]
    else:
        missing = " and ".join(
            label for label, seen in (("run", ran), ("bike", biked)) if not seen
        )
        lines += [
            f"**No {missing} Activity in this window.** The cardio path is",
            "unvalidated for it; widen `--days` before reading anything into",
            "the shapes below.",
            "",
        ]

    boundaries = dump_for(run_dir, HEART_RATE_ZONES_NAME)
    profiles = json_array_in(boundaries)
    lines += build_zone_boundaries_section(located, boundaries, profiles)
    lines += [
        "## Per Activity",
        "",
        "| # | typeKey | zone rows | boundaries | state |",
        "| --- | --- | --- | --- | --- |",
    ]
    for row, (act, dump) in enumerate(located, start=1):
        rows = json_array_in(dump)
        if dump is None:
            count, against, state = "-", "-", "missing"
        elif rows is None:
            count, against, state = "unreadable", "-", "on disk"
        else:
            count = str(len(rows))
            against = zone_attribution(profiles or [], zone_floors(rows))
            state = "on disk"
        lines.append(f"| {row} | {type_key(act)} | {count} | {against} | {state} |")

    missing_dumps = len(cardio) - len(on_disk)
    if missing_dumps:
        lines += [
            "",
            (
                f"**{missing_dumps} of them have no dump in this run "
                "directory.** Either the pass has not been run live over this "
                "window, or it stopped early - an `ABORT-*.txt` beside the "
                "dumps says which."
            ),
        ]

    return "\n".join(lines) + "\n"


def build_zone_boundaries_section(
    located: list[tuple[dict[str, Any], Path | None]],
    dump: Path | None,
    profiles: list[dict[str, Any]] | None,
) -> list[str]:
    """What the one zone-boundaries call returned, profile by profile."""
    lines = ["## Zone boundaries", ""]
    if dump is None:
        return lines + [
            "**Not fetched in this run directory.** Without them a",
            "`hrTimeInZones` dump is seconds against zone numbers that mean",
            "nothing.",
            "",
        ]

    lines += [
        f"One call to `{HEART_RATE_ZONES_PATH}`, dumped to `{dump.name}`.",
        "",
    ]
    if profiles is None:
        return lines + [
            "**The payload is not the array of profiles this note expects.**",
            "Read the dump.",
            "",
        ]

    lines += [
        "| sport profile | trainingMethod | zones |",
        "| --- | --- | --- |",
    ]
    for profile in profiles:
        lines.append(
            f"| `{profile.get('sport')}` | `{profile.get('trainingMethod')}` "
            f"| {len(profile_floors(profile))} |"
        )
    lines.append("")

    if len(profiles) == 1:
        lines += [
            f"Only the `{profiles[0].get('sport')}` profile is configured, so",
            "there is no per-sport override to get wrong here. That is this",
            "athlete's configuration rather than a property of the endpoint:",
            "the response is an array, and each entry names its `sport`, so",
            "another athlete's could hold several.",
            "",
        ]

    scored = {
        floors
        for _, activity_dump in located
        if (floors := zone_floors(json_array_in(activity_dump)))
    }
    configured = {floors for profile in profiles if (floors := profile_floors(profile))}
    if scored and scored <= configured:
        lines += [
            "Every `hrTimeInZones` row carries its own `zoneLowBoundary`, and",
            "every cardio dump in this window matches a configured profile, so",
            "each Activity's zones are identified rather than assumed. What",
            "that implies for the sync is settled in ADR 0008: the seconds are",
            "read off the Activity list and this endpoint is not called at all.",
            "",
        ]
    elif scored:
        lines += [
            "**At least one cardio dump was scored against boundaries that no",
            "configured profile states.** Zones have been changed since those",
            "Activities were recorded, or a profile is missing from the",
            "boundaries call. Either way, comparing them is unsafe until it is",
            "understood.",
            "",
        ]
    return lines


def build_field_check(
    pages: list[tuple[Path, list[dict[str, Any]]]],
    start: date,
    end: date,
) -> str:
    """Record whether the list entry carries the strength totals.

    The answer spec 03 needs, per .scratch/01-strength-data-spike/issues/
    01-authenticated-dump-harness.md.
    """
    activities = [(path, act) for path, page in pages for act in page]
    total = len(activities)
    strength = [(path, act) for path, act in activities if is_strength(act)]

    lines = note_header(
        "Activity list field check",
        (
            f"Window {start.isoformat()} to {end.isoformat()}: {total} Activities "
            f"over {len(pages)} page(s), {len(strength)} of them strength."
        ),
    ) + [
        "Read off the raw page dumps under `dumps/`, not off Garmin's",
        "documentation. Activity identifiers are deliberately absent here: they",
        "are in the dumps, which are not committed.",
        "",
        "## Across every Activity in the window",
        "",
        "| Field | Key present | Non-null | Carries a value |",
        "| --- | --- | --- | --- |",
    ]
    for field in LIST_FIELDS_IN_QUESTION:
        present = sum(1 for _, act in activities if field in act)
        non_null = sum(1 for _, act in activities if act.get(field) is not None)
        # A zero total and an empty summary array are both "present but says
        # nothing", which is the case spec 03 has to plan around.
        populated = sum(1 for _, act in activities if act.get(field))
        lines.append(
            f"| `{field}` | {present} / {total} | {non_null} / {total} "
            f"| {populated} / {total} |"
        )

    lines += ["", "## Strength Activities", ""]
    if not strength:
        lines.append(
            "None in this window. The question is unanswered for this athlete "
            "until the window is widened."
        )
    else:
        totals_header = " | ".join(f"`{field}`" for field in STRENGTH_TOTALS)
        lines += [
            f"| # | typeKey | {totals_header} | source file |",
            "| --- | --- | --- | --- | --- | --- |",
        ]
        for row, (path, act) in enumerate(strength, start=1):
            values = " | ".join(repr(act.get(field)) for field in STRENGTH_TOTALS)
            lines.append(f"| {row} | {type_key(act)} | {values} | `{path.name}` |")

    return "\n".join(lines) + "\n"


def parse_args(argv: list[str] | None = None) -> argparse.Namespace:
    here = Path(__file__).resolve().parent
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument(
        "--days", type=int, default=14, help="window length in days (default 14)"
    )
    parser.add_argument(
        "--pace",
        type=float,
        default=PacedReader.DEFAULT_PACE_SECONDS,
        help=f"seconds between calls (default {PacedReader.DEFAULT_PACE_SECONDS})",
    )
    parser.add_argument("--token-dir", default=None, help="overrides $GARMINTOKENS")
    parser.add_argument("--out", type=Path, default=here / "dumps")
    parser.add_argument("--field-check", type=Path, default=here / FIELD_CHECK_NAME)
    parser.add_argument("--coverage", type=Path, default=here / COVERAGE_NAME)
    parser.add_argument("--zone-coverage", type=Path, default=here / ZONE_COVERAGE_NAME)
    parser.add_argument(
        "--from-dumps",
        action="store_true",
        help="rebuild the notes from --out, with no Garmin call at all",
    )
    return parser.parse_args(argv)


def latest_run(out_dir: Path) -> Path:
    runs = sorted(path for path in out_dir.glob("*") if path.is_dir())
    if not runs:
        raise RuntimeError(f"No runs under {out_dir}; fetch something first.")
    return runs[-1]


def window_on_disk(run_dir: Path) -> tuple[date, date] | None:
    """The window a run actually fetched, which --days cannot be trusted for."""
    index = run_dir / "index.json"
    if not index.exists():
        return None
    for entry in json.loads(index.read_text("utf-8")):
        params = entry.get("params") or {}
        if params.get("startDate") and params.get("endDate"):
            return (
                date.fromisoformat(params["startDate"]),
                date.fromisoformat(params["endDate"]),
            )
    return None


def strength_activities(
    pages: list[tuple[Path, list[dict[str, Any]]]],
) -> list[dict[str, Any]]:
    return [act for _, page in pages for act in page if is_strength(act)]


def cardio_activities(
    pages: list[tuple[Path, list[dict[str, Any]]]],
) -> list[dict[str, Any]]:
    """The cardio Activities: everything the ExerciseSet pass did not take.

    Defined as the complement rather than as a list of `typeKey`s, so no
    Activity is fetched twice and none is skipped by an unexpected one.
    """
    return [act for _, page in pages for act in page if not is_strength(act)]


def pages_on_disk(run_dir: Path) -> list[tuple[Path, list[dict[str, Any]]]]:
    pages = []
    for path in sorted(run_dir.glob(f"{ACTIVITY_LIST_NAME}__page-*.json")):
        entries = json.loads(path.read_text("utf-8"))
        if entries:
            pages.append((path, entries))
    return pages


def write_notes(
    writer: DumpWriter,
    pages: list[tuple[Path, list[dict[str, Any]]]],
    start: date,
    end: date,
    field_check: Path,
    coverage: Path,
    zone_coverage: Path,
) -> None:
    """The three tracked notes, and the identifier-carrying one in the run dir."""
    strength = strength_activities(pages)
    field_check.write_text(build_field_check(pages, start, end), "utf-8")
    coverage.write_text(
        build_exercise_set_coverage(strength, writer.out_dir, start, end), "utf-8"
    )
    zone_coverage.write_text(
        build_heart_rate_zone_coverage(
            cardio_activities(pages), writer.out_dir, start, end
        ),
        "utf-8",
    )
    writer.write_note(CASES_NAME, build_exercise_set_cases(strength))


def main(argv: list[str] | None = None) -> int:
    args = parse_args(argv)
    if args.pace < 3.0:
        print(
            f"Refusing to pace faster than 3s (asked for {args.pace}s).",
            file=sys.stderr,
        )
        return 2

    end = date.today()
    start = end - timedelta(days=args.days)

    if args.from_dumps:
        try:
            run_dir = latest_run(args.out)
        except RuntimeError as error:
            print(error, file=sys.stderr)
            return 2
        pages = pages_on_disk(run_dir)
        start, end = window_on_disk(run_dir) or (start, end)
        write_notes(
            DumpWriter(run_dir),
            pages,
            start,
            end,
            args.field_check,
            args.coverage,
            args.zone_coverage,
        )
        print(
            f"Rebuilt from {len(pages)} page(s) in {run_dir}"
            f"\n{args.field_check}\n{args.coverage}"
            f"\n{args.zone_coverage}\n{run_dir / CASES_NAME}"
        )
        return 0

    # One directory per run. A later run over a shifted window must not
    # overwrite an earlier Activity's bytes: re-fetching is the one thing the
    # spike exists to avoid.
    writer = DumpWriter(args.out / datetime.now(UTC).strftime("%Y%m%dT%H%M%SZ"))

    try:
        client = authenticate(resolve_token_dir(args.token_dir))
    except Exception as error:  # noqa: BLE001 - a spike reports and stops
        print(
            f"\nLogin failed, stopping: {type(error).__name__}: {error}",
            file=sys.stderr,
        )
        return 2

    reader = reader_for(client, args.pace)
    print(
        f"\nActivity list {start.isoformat()} to {end.isoformat()}, "
        f"{args.pace}s between calls."
    )

    try:
        pages = list(fetch_activity_list(reader, writer, start, end))
        print("\nHeart-rate zone boundaries, once.")
        fetch_heart_rate_zones(reader, writer)
        strength = strength_activities(pages)
        print(f"\nExerciseSets for {len(strength)} strength Activities.")
        fetch_exercise_sets(reader, writer, strength)
        cardio = cardio_activities(pages)
        print(f"\nhrTimeInZones for {len(cardio)} cardio Activities.")
        fetch_hr_time_in_zones(reader, writer, cardio)
    except AbortSignal as abort:
        stamp = datetime.now(UTC).strftime("%Y%m%dT%H%M%SZ")
        note = writer.write_note(
            f"ABORT-{abort.status_code}-{stamp}.txt", abort.report() + "\n"
        )
        print(
            f"\nSTOPPED after {reader.call_count} call(s), without retrying."
            f"\n\n{abort.report()}\n\nWritten to {note}"
            "\n\nThe notes are left as they were; what did land is in the run "
            "directory, and --from-dumps rebuilds them from it.",
            file=sys.stderr,
        )
        return 2
    except RuntimeError as error:
        print(f"\n{error}", file=sys.stderr)
        return 2

    write_notes(
        writer,
        pages,
        start,
        end,
        args.field_check,
        args.coverage,
        args.zone_coverage,
    )
    print(
        f"\n{reader.call_count} call(s), {sum(len(page) for _, page in pages)} "
        f"Activities.\nDumps and index: {writer.out_dir}"
        f"\nField check: {args.field_check}"
        f"\nExerciseSet coverage: {args.coverage}"
        f"\nHeart-rate zone coverage: {args.zone_coverage}"
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
