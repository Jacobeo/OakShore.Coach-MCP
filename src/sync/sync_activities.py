"""Manually sync Activities for an explicit inclusive date range."""

from __future__ import annotations

import argparse
import json
import os
from datetime import date, datetime, timezone
from typing import Any, Callable
from urllib.request import Request, urlopen

from garmin_client import GarminActivityClient, load_token_client


def activity_from_garmin(entry: dict[str, Any]) -> dict[str, Any]:
    started = datetime.strptime(entry["startTimeGMT"], "%Y-%m-%d %H:%M:%S")
    activity: dict[str, Any] = {
        "activityId": entry["activityId"],
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


def post_to_ingest(url: str, token: str, batch: dict[str, Any]) -> None:
    if not url.startswith("https://"):
        raise ValueError("ingest URL must use HTTPS")
    request = Request(
        url, data=json.dumps(batch).encode("utf-8"), method="POST",
        headers={"Content-Type": "application/json", "Authorization": f"Bearer {token}"},
    )
    with urlopen(request, timeout=60) as response:
        if response.status != 200:
            raise RuntimeError(f"Ingest returned HTTP {response.status}")


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--from-date", required=True, type=date.fromisoformat)
    parser.add_argument("--to-date", required=True, type=date.fromisoformat)
    parser.add_argument("--token-dir", help="overrides GARMINTOKENS and ~/.garminconnect")
    parser.add_argument("--pace", type=float, default=4.0)
    parser.add_argument("--ingest-url", default=os.environ.get("COACH_INGEST_URL"))
    args = parser.parse_args()
    if not args.ingest_url:
        parser.error("--ingest-url or COACH_INGEST_URL is required")
    token = os.environ.get("COACH_ACCESS_TOKEN")
    if not token:
        parser.error("COACH_ACCESS_TOKEN is required")
    if args.from_date > args.to_date:
        parser.error("--from-date must be no later than --to-date")
    garmin = GarminActivityClient.from_login(load_token_client(args.token_dir, args.pace), args.pace)
    count = run(args.from_date, args.to_date, garmin,
                lambda batch: post_to_ingest(args.ingest_url, token, batch))
    print(f"Stored {count} Activities for {args.from_date} to {args.to_date}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
