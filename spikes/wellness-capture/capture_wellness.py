#!/usr/bin/env python3
"""Paced, read-only captures of Garmin's per-date endpoints, for tickets 05 and 06
of spec 03. Raw bytes go to the gitignored dumps/ beside this file;
`scrub_wellness.py` turns a wellness run into committed fixtures. Run from the
home machine only (ADR 0001). Each plan is fixed and printed before the first
request, and every plan starts with the token refresh when the saved token is
due one, through the sync's own `load_token_client`, so it is paced and is the
first network act.

`--plan wellness` (ticket 05):
- the social profile once, for the display name the sleep and summary paths need
- sleep, HRV and the daily summary for each day in DAYS, then for EMPTY_DAY
- the dedicated body battery and stress endpoints once, for PREMISE_DAY only,
  to check what they carry beyond the daily summary

`--plan form` (ticket 06): training status, TrainingReadiness and VO2max for
PREMISE_DAY, and the latest cycling FTP, to learn whether this watch produces
them before ticket 06 is planned.

Any status other than 200 or 204 stops the run without retrying (ADR 0005).

    python capture_wellness.py --plan wellness
"""

from __future__ import annotations

import argparse
import json
import sys
import time
from datetime import UTC, date, datetime
from pathlib import Path
from typing import Any, Callable
from urllib.parse import quote

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "src" / "sync"))

from garmin_client import GarminActivityClient, load_token_client  # noqa: E402

# The last three days of the Activity fixture window, so wellness and Activities
# fixtures describe the same days.
DAYS = (date(2026, 9, 21), date(2026, 9, 20), date(2026, 9, 19))
# Before this athlete's account existed: Garmin's answer for a day holding nothing.
# Last, because it is the request most likely to be refused.
EMPTY_DAY = date(2010, 1, 1)
PREMISE_DAY = date(2026, 9, 21)

PROFILE = ("userprofile-service--socialProfile", "/userprofile-service/socialProfile", None)


def day_requests(day: date, display_name: str) -> list[tuple[str, str, dict[str, str] | None]]:
    iso = day.isoformat()
    return [
        (f"wellness-service--dailySleepData__date-{iso}",
         f"/wellness-service/wellness/dailySleepData/{display_name}",
         {"date": iso, "nonSleepBufferMinutes": "60"}),
        (f"hrv-service--hrv__date-{iso}", f"/hrv-service/hrv/{iso}", None),
        (f"usersummary-service--daily__date-{iso}",
         f"/usersummary-service/usersummary/daily/{display_name}", {"calendarDate": iso}),
    ]


def premise_requests(day: date) -> list[tuple[str, str, dict[str, str] | None]]:
    iso = day.isoformat()
    return [
        (f"wellness-service--bodyBattery-reports-daily__date-{iso}",
         "/wellness-service/wellness/bodyBattery/reports/daily", {"startDate": iso, "endDate": iso}),
        (f"wellness-service--dailyStress__date-{iso}", f"/wellness-service/wellness/dailyStress/{iso}", None),
    ]


def form_requests(day: date) -> list[tuple[str, str, dict[str, str] | None]]:
    iso = day.isoformat()
    return [
        (f"metrics-service--trainingstatus-aggregated__date-{iso}",
         f"/metrics-service/metrics/trainingstatus/aggregated/{iso}", None),
        (f"metrics-service--trainingreadiness__date-{iso}", f"/metrics-service/metrics/trainingreadiness/{iso}", None),
        (f"metrics-service--maxmet-daily__date-{iso}", f"/metrics-service/metrics/maxmet/daily/{iso}/{iso}", None),
        ("biometric-service--latestFunctionalThresholdPower-CYCLING",
         "/biometric-service/biometric/latestFunctionalThresholdPower/CYCLING", None),
    ]


class Refused(RuntimeError):
    def __init__(self, status: int, name: str, body: bytes) -> None:
        super().__init__(f"HTTP {status} for {name}; capture stopped without retrying")
        self.body = body


class PacedReader:
    """GET-only and paced; anything but 200 or 204 stops the run."""

    def __init__(self, session: Any, base_url: str, headers: Callable[[], dict[str, str]],
                 pace_seconds: float, sleep: Callable[[float], None] = time.sleep) -> None:
        self._session = session
        self._base_url = base_url.rstrip("/")
        self._headers = headers
        self._pace_seconds = pace_seconds
        self._sleep = sleep
        self.calls = 0

    def get(self, name: str, path: str, params: dict[str, str] | None) -> tuple[int, bytes]:
        self._sleep(self._pace_seconds)
        response = self._session.request("GET", f"{self._base_url}{path}", headers=self._headers(),
                                         params=params, timeout=30, allow_redirects=False)
        self.calls += 1
        if response.status_code not in (200, 204):
            raise Refused(response.status_code, name, response.content)
        return response.status_code, response.content


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--plan", choices=("wellness", "form"), required=True)
    parser.add_argument("--token-dir", help="overrides GARMINTOKENS and ~/.garminconnect")
    parser.add_argument("--pace", type=float, default=4.0)
    parser.add_argument("--out", default=str(Path(__file__).with_name("dumps")))
    args = parser.parse_args(argv)
    if args.pace < GarminActivityClient.MIN_PACE_SECONDS:
        parser.error("Garmin requests must be paced at least 3 seconds apart")

    planned = 1 + 3 * (len(DAYS) + 1) + 2 if args.plan == "wellness" else len(form_requests(PREMISE_DAY))
    print(f"Plan: token refresh if due, then {planned} paced GETs at {args.pace}s")
    try:
        client = load_token_client(args.token_dir, args.pace)
    except Exception as error:
        print(f"Token load or refresh failed; an interactive login is needed. Stopped: {error}")
        return 2

    reader = PacedReader(client.client._api_session, client.client._connectapi,
                         client.client.get_api_headers, args.pace)
    run_dir = Path(args.out) / datetime.now(UTC).strftime("%Y%m%dT%H%M%SZ")
    run_dir.mkdir(parents=True)
    index: list[dict[str, Any]] = []

    def fetch(name: str, path: str, params: dict[str, str] | None) -> bytes:
        status, body = reader.get(name, path, params)
        (run_dir / f"{name}.json").write_bytes(body)
        index.append({"file": f"{name}.json", "endpoint": path, "params": params, "status": status,
                      "bytes": len(body), "fetchedAtUtc": datetime.now(UTC).isoformat(timespec="seconds")})
        print(f"{status} {name} ({len(body)} bytes)")
        return body

    try:
        if args.plan == "form":
            for request in form_requests(PREMISE_DAY):
                fetch(*request)
            return 0
        profile = json.loads(fetch(*PROFILE))
        display_name = quote(profile["displayName"], safe="")
        for day in DAYS:
            for request in day_requests(day, display_name):
                fetch(*request)
        for request in premise_requests(PREMISE_DAY):
            fetch(*request)
        for request in day_requests(EMPTY_DAY, display_name):
            fetch(*request)
    except Refused as error:
        print(error)
        print(error.body[:2000].decode("utf-8", "replace"))
        return 1
    finally:
        (run_dir / "index.json").write_text(json.dumps(index, indent=2), encoding="utf-8")
        print(f"{reader.calls} GETs; raw run at {run_dir}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
