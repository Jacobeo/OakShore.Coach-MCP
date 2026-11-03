"""Sync Activities, daily wellness and daily form, one day at a time, most recent
first, bounded per run, posting each day's Activities, SleepNight, DailySummary,
TrainingLoad and VO2max estimates as one version 4 batch. A run without dates
continues from the Watermark; an explicit inclusive date range syncs those days,
including any already completed. Each strength Activity costs one further call for
its ExerciseSets, posted with weight in kilograms. Cardio Activities carry time in
zones off the list entry, and the zone boundaries behind them are fetched at most
once per run (ADR 0008). A day costs three calls beyond the Activity list: sleep,
the daily summary and training status (ADR 0010, ADR 0011, ADR 0012)."""

from __future__ import annotations

import argparse
import json
import os
import re
import sys
from datetime import date, datetime, timedelta, timezone
from pathlib import Path
from typing import Any, Callable
from urllib.error import HTTPError
from urllib.request import Request, urlopen

import sync_sign_in
from garmin_client import GarminActivityClient, load_token_client
from notify import notifier_from_environment, run_notifying_failures
from sync_slot import AlreadyRunning, sync_slot
from watermark import Watermark


# Cardio is the complement of strength, so an unanticipated sport is posted unenriched.
STRENGTH_TYPE_KEYS = frozenset({"strength_training"})
VO2MAX_SPORTS = ("generic", "cycling")
TIME_IN_HEART_RATE_ZONE_FIELD = re.compile(r"hrTimeInZone_(\d+)")
HEART_RATE_ZONE_FLOOR_FIELD = re.compile(r"zone(\d+)Floor")


def numbered_fields(entry: dict[str, Any], pattern: re.Pattern[str]) -> list[tuple[int, Any]]:
    # Read the numbered fields that are there rather than assuming five; an absent or
    # null one is unknown, never zero.
    found = [(int(match.group(1)), value) for key, value in entry.items()
             if (match := pattern.fullmatch(key)) and value is not None]
    return sorted(found)


def time_in_heart_rate_zones_from_garmin(entry: dict[str, Any]) -> list[dict[str, Any]]:
    return [{"zoneNumber": number, "seconds": seconds}
            for number, seconds in numbered_fields(entry, TIME_IN_HEART_RATE_ZONE_FIELD)]


def sport_profile_from_garmin(entry: dict[str, Any]) -> dict[str, Any]:
    return {
        "name": entry.get("sport"),
        "trainingMethod": entry.get("trainingMethod"),
        "restingHeartRateBpm": entry.get("restingHeartRateUsed"),
        "maxHeartRateBpm": entry.get("maxHeartRateUsed"),
        "lactateThresholdHeartRateBpm": entry.get("lactateThresholdHeartRateUsed"),
        "heartRateZones": [{"zoneNumber": number, "lowBoundaryBpm": floor}
                           for number, floor in numbered_fields(entry, HEART_RATE_ZONE_FLOOR_FIELD)],
    }


def sport_profiles_once(garmin: GarminActivityClient) -> Callable[[], list[dict[str, Any]]]:
    fetched: list[list[dict[str, Any]]] = []

    def get() -> list[dict[str, Any]]:
        if not fetched:
            # A profile with no name or no floors measures nothing, so it is left out;
            # without a usable DEFAULT no Activity can be measured and the run stops.
            sport_profiles = [sport_profile for sport_profile in map(sport_profile_from_garmin,
                                                                     garmin.get_heart_rate_zones())
                              if sport_profile["name"] and sport_profile["heartRateZones"]]
            if not any(sport_profile["name"] == "DEFAULT" for sport_profile in sport_profiles):
                raise RuntimeError("Garmin heart rate zones carry no DEFAULT SportProfile with boundaries")
            fetched.append(sport_profiles)
        return fetched[0]
    return get


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


def exercise_set_start_utc(value: str | None) -> str | None:
    if value is None:
        return None
    # Per-set startTime is GMT with no offset marker; reading it as local time would shift every set.
    started = datetime.fromisoformat(value)
    return started.replace(tzinfo=timezone.utc).isoformat().replace("+00:00", "Z")


def exercise_set_from_garmin(entry: dict[str, Any]) -> dict[str, Any]:
    candidates = entry.get("exercises") or []
    top = candidates[0] if candidates else None
    weight_grams = entry.get("weight")
    # Garmin's -1 states the load was the athlete's own mass; 0 is a set performed with
    # no external load; null is nothing performed. Three answers, never collapsed.
    bodyweight = weight_grams == -1.0
    return {
        "setType": entry["setType"],
        "repetitions": entry.get("repetitionCount"),
        "weightKg": None if weight_grams is None or bodyweight else weight_grams / 1000,
        "bodyweight": bodyweight,
        "exercise": None if top is None else {"category": top["category"], "name": top.get("name")},
        "candidateCount": len(candidates),
        "topProbability": None if top is None else top.get("probability"),
        "startTimeUtc": exercise_set_start_utc(entry.get("startTime")),
        "durationSeconds": entry.get("duration"),
        "wktStepIndex": entry.get("wktStepIndex"),
    }


def without_arrays(value: Any) -> Any:
    # Intraday series are out of scope (spec 03); the scalars around them are archived.
    if isinstance(value, dict):
        return {key: without_arrays(item) for key, item in value.items() if not isinstance(item, list)}
    return value


def utc_from_epoch_ms(value: int) -> str:
    return datetime.fromtimestamp(value / 1000, timezone.utc).isoformat().replace("+00:00", "Z")


def whole_minutes(seconds: int | None) -> int | None:
    # Sleep is read in minutes; Garmin's seconds are converted once, here.
    return None if seconds is None else round(seconds / 60)


def same_day(day: date, dated: Any, kind: str) -> None:
    if dated != day.isoformat():
        raise RuntimeError(f"Garmin {kind} for {day.isoformat()} was dated {dated}")


def no_later_than(day: date, dated: Any, kind: str) -> date:
    if not isinstance(dated, str) or date.fromisoformat(dated) > day:
        raise RuntimeError(f"Garmin {kind} for {day.isoformat()} was dated {dated}")
    return date.fromisoformat(dated)


def sleep_night_from_garmin(day: date, payload: dict[str, Any] | None) -> dict[str, Any] | None:
    sleep = (payload or {}).get("dailySleepDTO") or {}
    start, end = sleep.get("sleepStartTimestampGMT"), sleep.get("sleepEndTimestampGMT")
    # Garmin answers a night it holds nothing for with a DTO of nulls.
    if start is None and end is None:
        return None
    if start is None or end is None:
        raise RuntimeError(f"Garmin sleep for {day.isoformat()} has a start or an end but not both")
    same_day(day, sleep.get("calendarDate"), "sleep")
    return {
        "calendarDate": day.isoformat(),
        "startTimeUtc": utc_from_epoch_ms(start),
        "endTimeUtc": utc_from_epoch_ms(end),
        "sleepMinutes": whole_minutes(sleep.get("sleepTimeSeconds")),
        "deepSleepMinutes": whole_minutes(sleep.get("deepSleepSeconds")),
        "lightSleepMinutes": whole_minutes(sleep.get("lightSleepSeconds")),
        "remSleepMinutes": whole_minutes(sleep.get("remSleepSeconds")),
        "awakeSleepMinutes": whole_minutes(sleep.get("awakeSleepSeconds")),
        "unmeasurableSleepMinutes": whole_minutes(sleep.get("unmeasurableSleepSeconds")),
        "garminResponse": without_arrays(payload),
    }


def daily_summary_from_garmin(day: date, payload: dict[str, Any] | None) -> dict[str, Any] | None:
    if payload is None:
        return None
    # Hidden data is not an empty day; completing it would never fetch it again.
    if payload.get("privacyProtected") is True:
        raise RuntimeError(f"Garmin daily summary for {day.isoformat()} is privacy protected")
    if payload.get("includesWellnessData") is not True and payload.get("includesActivityData") is not True:
        return None
    same_day(day, payload.get("calendarDate"), "daily summary")
    return {
        "calendarDate": day.isoformat(),
        "bodyBattery": {
            "charged": payload.get("bodyBatteryChargedValue"),
            "drained": payload.get("bodyBatteryDrainedValue"),
            "highest": payload.get("bodyBatteryHighestValue"),
            "lowest": payload.get("bodyBatteryLowestValue"),
        },
        "stressLevel": {
            "average": payload.get("averageStressLevel"),
            "maximum": payload.get("maxStressLevel"),
            "restSeconds": payload.get("restStressDuration"),
            "lowSeconds": payload.get("lowStressDuration"),
            "mediumSeconds": payload.get("mediumStressDuration"),
            "highSeconds": payload.get("highStressDuration"),
        },
        "garminResponse": without_arrays(payload),
    }


def training_load_from_garmin(day: date, payload: dict[str, Any] | None) -> dict[str, Any] | None:
    by_device = ((payload or {}).get("mostRecentTrainingStatus") or {}).get("latestTrainingStatusData") or {}
    # Choosing one watch's figures over another's would be a guess at which to trust.
    if len(by_device) > 1:
        raise RuntimeError(f"Garmin training status for {day.isoformat()} holds {len(by_device)} devices;"
                           " the sync stores one watch's TrainingLoad and will not choose between them")
    if not by_device:
        return None
    (status,) = by_device.values()
    # An earlier date is the last status Garmin holds, not one of this day's own.
    if no_later_than(day, status.get("calendarDate"), "training status") < day:
        return None
    figures = {key: status.get(key) for key in ("weeklyTrainingLoad", "loadTunnelMin", "loadTunnelMax")}
    if all(value is None for value in figures.values()):
        return None
    return {"calendarDate": day.isoformat(), **figures, "garminResponse": without_arrays(payload)}


def vo2max_estimates_from_garmin(day: date, payload: dict[str, Any] | None) -> list[dict[str, Any]]:
    by_sport = (payload or {}).get("mostRecentVO2Max") or {}
    estimates = []
    for sport in VO2MAX_SPORTS:
        estimate = by_sport.get(sport) or {}
        if estimate.get("vo2MaxPreciseValue") is None:
            continue
        # Dated by when Garmin computed it, so it may precede the day read but never follow it.
        dated = no_later_than(day, estimate.get("calendarDate"), f"{sport} VO2max read")
        estimates.append({"sport": sport, "calendarDate": dated.isoformat(),
                          "mlPerKgMin": estimate["vo2MaxPreciseValue"]})
    return estimates


def activity_batch(
    day: date,
    garmin: GarminActivityClient,
    sport_profiles: Callable[[], list[dict[str, Any]]],
) -> dict[str, Any]:
    activities = []
    for entry in garmin.list_activities(day, day):
        activity = activity_from_garmin(entry)
        if activity["typeKey"] not in STRENGTH_TYPE_KEYS and (
                time_in_heart_rate_zones := time_in_heart_rate_zones_from_garmin(entry)):
            activity["timeInHeartRateZones"] = time_in_heart_rate_zones
        activities.append(activity)
    batch: dict[str, Any] = {
        "activityRange": {"fromDate": day.isoformat(), "toDate": day.isoformat()},
        "activities": activities,
    }
    if any("timeInHeartRateZones" in activity for activity in activities):
        batch["sportProfiles"] = sport_profiles()
    for activity in activities:
        if activity["typeKey"] in STRENGTH_TYPE_KEYS:
            activity["exerciseSets"] = [exercise_set_from_garmin(item)
                                        for item in garmin.get_exercise_sets(activity["activityId"])]
    return batch


def day_batch(
    day: date,
    garmin: GarminActivityClient,
    sport_profiles: Callable[[], list[dict[str, Any]]],
) -> dict[str, Any]:
    batch = activity_batch(day, garmin, sport_profiles)
    sleep_night = sleep_night_from_garmin(day, garmin.get_sleep_night(day))
    daily_summary = daily_summary_from_garmin(day, garmin.get_daily_summary(day))
    training_status = garmin.get_training_status(day)
    training_load = training_load_from_garmin(day, training_status)
    # Version 4 states that every date in its range was synced, so empty lists record a
    # day Garmin holds nothing for rather than a day never attempted.
    batch["version"] = 4
    batch["sleepNights"] = [] if sleep_night is None else [sleep_night]
    batch["dailySummaries"] = [] if daily_summary is None else [daily_summary]
    batch["trainingLoads"] = [] if training_load is None else [training_load]
    batch["vo2maxEstimates"] = vo2max_estimates_from_garmin(day, training_status)
    return batch


def sync_days(
    days: list[date],
    today: date,
    max_days: int,
    watermark: Watermark,
    garmin: GarminActivityClient,
    post: Callable[[dict[str, Any]], Any],
) -> tuple[int, int, int]:
    """Sync days in the order given, up to the ceiling; returns (days, Activities, days left)."""
    if max_days < 1:
        raise ValueError("max_days must be positive")
    sport_profiles = sport_profiles_once(garmin)
    activity_count = 0
    for day in days[:max_days]:
        batch = day_batch(day, garmin, sport_profiles)
        post(batch)
        activity_count += len(batch["activities"])
        # Today is synced but never completed, so the next run picks up the rest of it.
        if day < today:
            watermark.mark_completed(day)
    days_synced = min(len(days), max_days)
    return days_synced, activity_count, len(days) - days_synced


def run(
    start: date,
    end: date,
    today: date,
    max_days: int,
    watermark: Watermark,
    garmin: GarminActivityClient,
    post: Callable[[dict[str, Any]], Any],
) -> tuple[int, int, int]:
    """Sync an explicit inclusive range most recent first, completed days included."""
    if start > end:
        raise ValueError("from date must be no later than to date")
    # A future day holds nothing yet, and completing it would skip it once it happened.
    if end > today:
        raise ValueError("to date must be no later than today")
    days = [end - timedelta(days=offset) for offset in range((end - start).days + 1)]
    return sync_days(days, today, max_days, watermark, garmin, post)


def run_watermark(
    today: date,
    earliest: date,
    max_days: int,
    watermark: Watermark,
    garmin: GarminActivityClient,
    post: Callable[[dict[str, Any]], Any],
) -> tuple[int, int, int]:
    """Sync the days the Watermark does not cover, most recent first."""
    return sync_days(watermark.pending_days(earliest, today), today, max_days, watermark, garmin, post)


def run_unless_already_running(
    slot_file: Path,
    sync: Callable[[], Any],
    notify: Callable[[Any], None],
    warn: Callable[[str], None] = lambda text: print(text, file=sys.stderr),
) -> int:
    """Run the sync unless another one holds the machine's slot; returns an exit code.

    The slot is taken outside the notification wrapper, so an overlap is logged and
    nothing else: the run already going covers the same days, and a message about it
    would be noise beside the one that means the athlete must act.
    """
    try:
        with sync_slot(slot_file):
            return run_notifying_failures(sync, notify)
    except AlreadyRunning as held:
        warn(f"{held}; it already covers these days, so this run stops here.")
        return 0


def post_to_ingest(url: str, token: Callable[[], str], batch: dict[str, Any]) -> None:
    if not url.startswith("https://"):
        raise ValueError("ingest URL must use HTTPS")
    # Asked for per batch, not per run: a catch-up of many days outlives one token.
    request = Request(
        url, data=json.dumps(batch).encode("utf-8"), method="POST",
        headers={"Content-Type": "application/json", "Authorization": f"Bearer {token()}"},
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
    parser.add_argument("--slot-file", default=os.environ.get("COACH_SYNC_SLOT")
                        or "~/.coach-sync/sync.lock",
                        help="the machine's one sync slot; a second run stops rather"
                             " than posting the same day twice")
    parser.add_argument("--sign-in-file", default=os.environ.get("COACH_SYNC_SIGNIN")
                        or sync_sign_in.DEFAULT_TOKEN_FILE,
                        help="where the sync's saved WorkOS sign-in is kept")
    parser.add_argument("--authority", default=os.environ.get("COACH_INGEST_AUTHORITY"),
                        help="the WorkOS issuer that signs ingest access tokens")
    parser.add_argument("--client-id", default=os.environ.get("COACH_INGEST_CLIENT_ID"),
                        help="the OAuth client this sync signs in as")
    parser.add_argument("--resource", default=os.environ.get("COACH_INGEST_RESOURCE"),
                        help="resource indicator, when the issuer's default is not"
                             " the ingest audience")
    parser.add_argument("--redirect-port", type=int,
                        default=int(os.environ.get("COACH_INGEST_REDIRECT_PORT")
                                    or sync_sign_in.DEFAULT_REDIRECT_PORT),
                        help="loopback port the sign-in redirect returns to")
    parser.add_argument("--login", action="store_true",
                        help="sign in interactively once, then exit; every later run"
                             " is unattended")
    args = parser.parse_args()
    sign_in_file = Path(args.sign_in_file).expanduser()
    slot_file = Path(args.slot_file).expanduser()
    if args.login:
        if not args.authority or not args.client_id:
            parser.error("--authority and --client-id are required to sign in")
        # Signing in writes the same file a running sync rotates, so it takes the
        # SyncSlot too rather than racing that write.
        try:
            with sync_slot(slot_file):
                sync_sign_in.sign_in(sign_in_file, args.authority, args.client_id,
                                    args.resource, args.redirect_port)
        except AlreadyRunning as held:
            print(f"{held}; wait for it to finish before signing in.", file=sys.stderr)
            return 1
        return 0
    if not args.ingest_url:
        parser.error("--ingest-url or COACH_INGEST_URL is required")
    # A token in the environment is the manual escape hatch; everything scheduled uses
    # the saved sign-in, because a token obtained by hand expires within minutes.
    static_token = os.environ.get("COACH_ACCESS_TOKEN")
    if not static_token and not (args.authority and args.client_id):
        parser.error("--authority and --client-id (or COACH_ACCESS_TOKEN) are required")
    explicit = args.from_date is not None or args.to_date is not None
    if explicit and (args.from_date is None or args.to_date is None):
        parser.error("--from-date and --to-date must be given together")
    if explicit and args.from_date > args.to_date:
        parser.error("--from-date must be no later than --to-date")
    if explicit and args.to_date > date.today():
        parser.error("--to-date must be no later than today")
    if not explicit and args.earliest_date is None:
        parser.error("--earliest-date or COACH_SYNC_EARLIEST_DATE is required"
                     " for a run without dates")
    if args.max_days < 1:
        parser.error("--max-days must be at least 1")
    # A bad command line is the athlete's own doing and has already been reported, so
    # only what happens from the token onwards is worth a notification.
    notify = notifier_from_environment()

    if static_token:
        token: Callable[[], str] = lambda: static_token
    else:
        token = sync_sign_in.SyncSignIn(sign_in_file, args.authority, args.client_id,
                                             args.resource).access_token

    def sync() -> None:
        garmin = GarminActivityClient.from_login(load_token_client(args.token_dir, args.pace),
                                                 args.pace)

        def post(batch: dict[str, Any]) -> None:
            post_to_ingest(args.ingest_url, token, batch)

        watermark = Watermark(Path(args.watermark_file).expanduser())
        if explicit:
            days, count, left = run(args.from_date, args.to_date, date.today(), args.max_days,
                                    watermark, garmin, post)
        else:
            days, count, left = run_watermark(date.today(), args.earliest_date, args.max_days,
                                              watermark, garmin, post)
        print(f"Synced {days} days ({count} Activities); {left} days remaining")

    return run_unless_already_running(slot_file, sync, notify)


if __name__ == "__main__":
    raise SystemExit(main())
