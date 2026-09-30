"""Sync Activities: a run without dates continues from the Watermark, most recent
day first, bounded per run; an explicit inclusive date range syncs just that range."""

from __future__ import annotations

import argparse
import json
import os
from datetime import date, datetime, timezone
from pathlib import Path
from typing import Any, Callable
from urllib.error import HTTPError
from urllib.request import Request, urlopen

from garmin_client import GarminActivityClient, load_token_client
from watermark import Watermark


def activity_from_garmin(entry: dict[str, Any]) -> dict[str, Any]:
    started = datetime.strptime(entry["startTimeGMT"], "%Y-%m-%d %H:%M:%S")
    activity: dict[str, Any] = {
        "activityId": entry["activityId"],
        "activityName": entry.get("activityName"),
        "startTimeUtc": started.replace(tzinfo=timezone.utc).isoformat().replace("+00:00", "Z"),
        "typeKey": entry["activityType"]["typeKey"],
        "durationSeconds": entry["duration"],
        "distanceMeters": entry.get("distance"),
        "totalSets": entry.get("totalSets"),
        "activeSets": entry.get("activeSets"),
        "totalReps": entry.get("totalReps"),
    }
    return activity


def run(
    start: date,
    end: date,
    garmin: GarminActivityClient,
    post: Callable[[dict[str, Any]], Any],
) -> int:
    if start > end:
        raise ValueError("from date must be no later than to date")
    activities = [activity_from_garmin(entry) for entry in garmin.list_activities(start, end)]
    post({
        "version": 2,
        "activityRange": {"fromDate": start.isoformat(), "toDate": end.isoformat()},
        "activities": activities,
    })
    return len(activities)


def run_watermark(
    today: date,
    earliest: date,
    max_days: int,
    watermark: Watermark,
    garmin: GarminActivityClient,
    post: Callable[[dict[str, Any]], Any],
) -> tuple[int, int, int]:
    """Sync pending days most recent first; returns (days, Activities, days left)."""
    if max_days < 1:
        raise ValueError("max_days must be positive")
    pending = watermark.pending_days(earliest, today)
    activity_count = 0
    for day in pending[:max_days]:
        activity_count += run(day, day, garmin, post)
        # Today is synced but never completed, so the next run picks up the rest of it.
        if day < today:
            watermark.mark_completed(day)
    days_synced = min(len(pending), max_days)
    return days_synced, activity_count, len(pending) - days_synced


def post_to_ingest(url: str, token: str, batch: dict[str, Any]) -> None:
    if not url.startswith("https://"):
        raise ValueError("ingest URL must use HTTPS")
    request = Request(
        url, data=json.dumps(batch).encode("utf-8"), method="POST",
        headers={"Content-Type": "application/json", "Authorization": f"Bearer {token}"},
    )
    try:
        with urlopen(request, timeout=60) as response:
            if response.status != 200:
                raise RuntimeError(f"Ingest returned HTTP {response.status}")
    except HTTPError as error:
        detail = error.read().decode("utf-8", "replace")[:2000]
        raise RuntimeError(f"Ingest returned HTTP {error.code}: {detail}") from None


def env_date(name: str) -> date | None:
    value = os.environ.get(name)
    return date.fromisoformat(value) if value else None


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--from-date", type=date.fromisoformat)
    parser.add_argument("--to-date", type=date.fromisoformat)
    parser.add_argument("--earliest-date", type=date.fromisoformat,
                        default=env_date("COACH_SYNC_EARLIEST_DATE"),
                        help="first day a run without dates may reach back to")
    parser.add_argument("--max-days", type=int,
                        default=int(os.environ.get("COACH_SYNC_MAX_DAYS", "30")),
                        help="ceiling on days processed per run")
    parser.add_argument("--watermark-file",
                        default=os.environ.get("COACH_SYNC_WATERMARK")
                        or "~/.coach-sync/watermark.json")
    parser.add_argument("--token-dir", help="overrides GARMINTOKENS and ~/.garminconnect")
    parser.add_argument("--pace", type=float, default=4.0)
    parser.add_argument("--ingest-url", default=os.environ.get("COACH_INGEST_URL"))
    args = parser.parse_args()
    if not args.ingest_url:
        parser.error("--ingest-url or COACH_INGEST_URL is required")
    token = os.environ.get("COACH_ACCESS_TOKEN")
    if not token:
        parser.error("COACH_ACCESS_TOKEN is required")
    explicit = args.from_date is not None or args.to_date is not None
    if explicit and (args.from_date is None or args.to_date is None):
        parser.error("--from-date and --to-date must be given together")
    if explicit and args.from_date > args.to_date:
        parser.error("--from-date must be no later than --to-date")
    if not explicit and args.earliest_date is None:
        parser.error("--earliest-date or COACH_SYNC_EARLIEST_DATE is required"
                     " for a run without dates")
    if args.max_days < 1:
        parser.error("--max-days must be at least 1")
    garmin = GarminActivityClient.from_login(load_token_client(args.token_dir, args.pace), args.pace)

    def post(batch: dict[str, Any]) -> None:
        post_to_ingest(args.ingest_url, token, batch)

    if explicit:
        count = run(args.from_date, args.to_date, garmin, post)
        print(f"Stored {count} Activities for {args.from_date} to {args.to_date}")
        return 0
    watermark = Watermark(Path(args.watermark_file).expanduser())
    days, count, left = run_watermark(date.today(), args.earliest_date, args.max_days,
                                      watermark, garmin, post)
    print(f"Synced {days} days ({count} Activities); {left} days remaining")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
