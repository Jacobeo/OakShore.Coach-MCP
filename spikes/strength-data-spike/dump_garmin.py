#!/usr/bin/env python3
"""Throwaway harness for the strength data spike (.scratch/01-strength-data-spike.md).

Logs in to Garmin Connect once by hand, persists the token, then dumps the raw
bytes of an Activity list over a recent window. Run it from the home machine
only: Garmin rate-limits by IP and datacenter ranges fare worse (ADR 0001).

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
FIELD_CHECK_NAME = "activity-list-field-check.md"
PAGE_SIZE = 20
# A few weeks of Activities is far under this. The cap exists so a server that
# never returns an empty page cannot loop us into a burst of requests.
MAX_PAGES = 50

STRENGTH_TOTALS = ("totalSets", "activeSets", "totalReps")


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
        name = endpoint_name
        if activity_id is not None:
            name += f"__activity-{activity_id}"
        if page is not None:
            name += f"__page-{page:02d}"
        target = self.out_dir / f"{name}.json"
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


def is_strength(activity: dict[str, Any]) -> bool:
    type_key = (activity.get("activityType") or {}).get("typeKey") or ""
    if "strength" in type_key.lower():
        return True
    # A non-zero strength total on an unexpected typeKey is still a strength
    # session; a zeroed one on a run is not.
    return any(activity.get(field) for field in STRENGTH_TOTALS)


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

    lines = [
        "# Activity list field check",
        "",
        (
            f"Generated by `dump_garmin.py` at "
            f"{datetime.now(UTC).isoformat(timespec='seconds')}."
        ),
        (
            f"Window {start.isoformat()} to {end.isoformat()}: {total} Activities "
            f"over {len(pages)} page(s), {len(strength)} of them strength."
        ),
        "",
        "Read off the raw page dumps under `dumps/`, not off Garmin's",
        "documentation. Activity identifiers are deliberately absent here: they",
        "are in the dumps, which are not committed.",
        "",
        "## Across every Activity in the window",
        "",
        "| Field | Key present | Non-null |",
        "| --- | --- | --- |",
    ]
    for field in STRENGTH_TOTALS:
        present = sum(1 for _, act in activities if field in act)
        non_null = sum(1 for _, act in activities if act.get(field) is not None)
        lines.append(f"| `{field}` | {present} / {total} | {non_null} / {total} |")

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
            lines.append(
                f"| {row} | {(act.get('activityType') or {}).get('typeKey')} "
                f"| {values} | `{path.name}` |"
            )

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
    return parser.parse_args(argv)


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
    writer = DumpWriter(args.out)

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
    except AbortSignal as abort:
        stamp = datetime.now(UTC).strftime("%Y%m%dT%H%M%SZ")
        note = writer.write_note(
            f"ABORT-{abort.status_code}-{stamp}.txt", abort.report() + "\n"
        )
        print(
            f"\nSTOPPED after {reader.call_count} call(s), without retrying."
            f"\n\n{abort.report()}\n\nWritten to {note}",
            file=sys.stderr,
        )
        return 2
    except RuntimeError as error:
        print(f"\n{error}", file=sys.stderr)
        return 2

    args.field_check.write_text(build_field_check(pages, start, end), "utf-8")
    print(
        f"\n{reader.call_count} call(s), {sum(len(page) for _, page in pages)} "
        f"Activities.\nDumps and index: {writer.out_dir}"
        f"\nField check: {args.field_check}"
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
