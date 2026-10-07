"""The only module that talks to Garmin Connect."""

from __future__ import annotations

import json
import os
import re
import time
from datetime import date
from pathlib import Path
from typing import Any, Callable


class GarminAbort(RuntimeError):
    def __init__(self, status: int) -> None:
        super().__init__(f"Garmin returned HTTP {status}; sync stopped")
        self.status = status


def load_token_client(
    token_dir: str | None = None,
    pace_seconds: float = 4.0,
    sleep: Callable[[float], None] = time.sleep,
) -> Any:
    from garminconnect import Garmin
    from garminconnect.client import token_file_path

    if pace_seconds < GarminActivityClient.MIN_PACE_SECONDS:
        raise ValueError("Garmin requests must be paced at least 3 seconds apart")
    location = Path(token_dir or os.environ.get("GARMINTOKENS") or "~/.garminconnect").expanduser()
    if not token_file_path(str(location)).is_file():
        raise RuntimeError(f"No saved Garmin token at {location}; complete interactive login first")
    client = Garmin()
    # Token loading is local; Garmin.login also fetches profile and settings.
    client.client.load(str(location))
    if not client.client.is_authenticated:
        raise RuntimeError("Saved Garmin token is not authenticated")
    if client.client._token_expires_soon():
        for attempt in range(3):
            sleep(pace_seconds)
            try:
                client.client._refresh_di_token()
                client.client.dump(str(location))
                break
            except OSError:
                if attempt == 2:
                    raise
            except Exception as error:
                match = re.search(r"(?:failed: |HTTP )(\d{3})", str(error))
                status = int(match.group(1)) if match else None
                if status not in range(500, 600) or attempt == 2:
                    raise
            sleep(2 ** attempt)
    return client


class GarminActivityClient:
    PAGE_SIZE = 20
    MAX_PAGES = 50
    MIN_PACE_SECONDS = 3.0

    def __init__(
        self,
        session: Any,
        base_url: str,
        headers: Callable[[], dict[str, str]],
        pace_seconds: float = 4.0,
        sleep: Callable[[float], None] = time.sleep,
        max_attempts: int = 3,
    ) -> None:
        if pace_seconds < self.MIN_PACE_SECONDS:
            raise ValueError("Garmin requests must be paced at least 3 seconds apart")
        if max_attempts < 1:
            raise ValueError("max_attempts must be positive")
        self._session = session
        self._base_url = base_url.rstrip("/")
        self._headers = headers
        self._pace_seconds = pace_seconds
        self._sleep = sleep
        self._max_attempts = max_attempts

    @classmethod
    def from_login(cls, client: Any, pace_seconds: float = 4.0) -> GarminActivityClient:
        return cls(client.client._api_session, client.client._connectapi,
                   client.client.get_api_headers, pace_seconds)

    def _get(self, path: str, params: dict[str, str]) -> Any:
        url = f"{self._base_url}/{path.lstrip('/')}"
        for attempt in range(self._max_attempts):
            # Keep the same pace after token load or refresh and between reads.
            self._sleep(self._pace_seconds)
            try:
                response = self._session.request(
                    "GET", url, headers=self._headers(), params=params,
                    timeout=30, allow_redirects=False,
                )
            except OSError:
                if attempt + 1 == self._max_attempts:
                    raise
                self._sleep(2 ** attempt)
                continue
            status = response.status_code
            if status in (401, 403, 429):
                raise GarminAbort(status)
            if status >= 500:
                if attempt + 1 == self._max_attempts:
                    raise GarminAbort(status)
                self._sleep(2 ** attempt)
                continue
            if status < 200 or status >= 300:
                raise GarminAbort(status)
            return json.loads(response.content)
        raise AssertionError("unreachable")

    def get_exercise_sets(self, activity_id: int) -> list[dict[str, Any]]:
        payload = self._get(f"/activity-service/activity/{activity_id}/exerciseSets", {})
        if not isinstance(payload, dict):
            raise RuntimeError("Garmin exercise sets response was not an object")
        exercise_sets = payload.get("exerciseSets") or []
        if not isinstance(exercise_sets, list) or any(not isinstance(item, dict) for item in exercise_sets):
            raise RuntimeError("Garmin exercise sets were not an array of objects")
        return exercise_sets

    def get_heart_rate_zones(self) -> list[dict[str, Any]]:
        payload = self._get("/biometric-service/heartRateZones", {})
        if not isinstance(payload, list) or any(not isinstance(item, dict) for item in payload):
            raise RuntimeError("Garmin heart rate zones were not an array of objects")
        return payload

    def list_activities(self, start: date, end: date) -> list[dict[str, Any]]:
        activities: list[dict[str, Any]] = []
        for page in range(self.MAX_PAGES):
            entries = self._get("/activitylist-service/activities/search/activities", {
                "startDate": start.isoformat(), "endDate": end.isoformat(),
                "start": str(page * self.PAGE_SIZE), "limit": str(self.PAGE_SIZE),
            })
            if not isinstance(entries, list) or any(not isinstance(item, dict) for item in entries):
                raise RuntimeError("Garmin Activity list was not an array of objects")
            activities.extend(entries)
            if len(entries) < self.PAGE_SIZE:
                return activities
        raise RuntimeError("Garmin Activity list exceeded 50 pages; narrow the date range")
