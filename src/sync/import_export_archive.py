"""Backfill history from Garmin's ExportArchive with no Garmin API calls: its
Activities, SleepNights and DailySummaries, posted to ingest as version 3 batches,
one per calendar month. Run it against the zip Garmin sends or the folder it was
unpacked to; re-running it posts the same batches, which ingest treats as the same
delivery."""

from __future__ import annotations

import argparse
import io
import json
import sys
import zipfile
from calendar import monthrange
from datetime import date, datetime, timezone
from fnmatch import fnmatchcase
from pathlib import Path, PurePosixPath
from typing import Any, Callable, Iterator

from sync_activities import (activity_from_garmin, add_machine_arguments, daily_summary_from_garmin,
                             ingest_token, post_to_ingest, sleep_night_from_garmin, without_arrays)
from sync_slot import AlreadyRunning, sync_slot


# Files are chunked by date or by count and named after the account, so each kind is
# found by pattern, every chunk of it read.
ACTIVITY_FILES = "*summarizedActivities*.json"
SLEEP_FILES = "*sleepData*.json"
DAILY_SUMMARY_FILES = "UDSFile*.json"
SLEEP_STAGES = ("deepSleepSeconds", "lightSleepSeconds", "remSleepSeconds")


def archive_files(archive: Path, patterns: tuple[str, ...]) -> Iterator[tuple[str, bytes]]:
    """Each file whose name matches one of the patterns, wherever it lies, as the pattern
    it matched and its content. Zips are opened at any depth."""
    if not archive.is_dir():
        with zipfile.ZipFile(archive) as zipped:
            yield from zip_files(zipped, patterns)
        return
    for path in sorted(path for path in archive.rglob("*") if path.is_file()):
        if path.suffix.lower() == ".zip":
            with zipfile.ZipFile(path) as zipped:
                yield from zip_files(zipped, patterns)
        elif pattern := matching(path.name, patterns):
            yield pattern, path.read_bytes()


def zip_files(zipped: zipfile.ZipFile, patterns: tuple[str, ...]) -> Iterator[tuple[str, bytes]]:
    for info in sorted(zipped.infolist(), key=lambda info: info.filename):
        name = PurePosixPath(info.filename).name
        if info.is_dir():
            continue
        if name.lower().endswith(".zip"):
            # Read whole, because seeking back inside a compressed member restarts its decompression.
            with zipfile.ZipFile(io.BytesIO(zipped.read(info))) as nested:
                yield from zip_files(nested, patterns)
        elif pattern := matching(name, patterns):
            yield pattern, zipped.read(info)


def matching(name: str, patterns: tuple[str, ...]) -> str | None:
    return next((pattern for pattern in patterns if fnmatchcase(name, pattern)), None)


# Each export record is translated into the field names and units of Garmin's API, then
# built by the sync's own builder, so an Activity or day synced both ways posts the same.

def activity_from_export(record: dict[str, Any]) -> dict[str, Any] | None:
    """An Activity without its time in zones: the export's hrTimeInZone_N add zones 0 and 6
    and split time differently from the list's, so the sync's stored seconds are kept."""
    if any(record.get(field) is None for field in ("activityId", "startTimeGmt", "activityType", "duration")):
        return None
    distance = record.get("distance")
    # The export counts epoch milliseconds, milliseconds and centimetres where the API's
    # list carries GMT text, seconds and metres.
    return activity_from_garmin({
        "activityId": record["activityId"],
        "activityName": record.get("name"),
        "startTimeGMT": datetime.fromtimestamp(record["startTimeGmt"] / 1000, timezone.utc)
        .strftime("%Y-%m-%d %H:%M:%S"),
        "activityType": {"typeKey": record["activityType"]},
        "duration": record["duration"] / 1000,
        "distance": None if distance is None else distance / 100,
        "totalSets": record.get("totalSets"),
        "activeSets": record.get("activeSets"),
        "totalReps": record.get("totalReps"),
    })


def epoch_ms(gmt: str) -> int:
    return round(datetime.fromisoformat(gmt).replace(tzinfo=timezone.utc).timestamp() * 1000)


def sleep_night_from_export(record: dict[str, Any]) -> dict[str, Any] | None:
    start, end = record.get("sleepStartTimestampGMT"), record.get("sleepEndTimestampGMT")
    if record.get("calendarDate") is None or start is None or end is None:
        return None
    # The export states no total. Garmin's sleep time is the deep, light and REM stages.
    stages = [record[stage] for stage in SLEEP_STAGES if record.get(stage) is not None]
    night = sleep_night_from_garmin(date.fromisoformat(record["calendarDate"]), {"dailySleepDTO": {
        "calendarDate": record["calendarDate"],
        "sleepStartTimestampGMT": epoch_ms(start),
        "sleepEndTimestampGMT": epoch_ms(end),
        "sleepTimeSeconds": sum(stages) if stages else None,
        "deepSleepSeconds": record.get("deepSleepSeconds"),
        "lightSleepSeconds": record.get("lightSleepSeconds"),
        "remSleepSeconds": record.get("remSleepSeconds"),
        "awakeSleepSeconds": record.get("awakeSleepSeconds"),
        "unmeasurableSleepSeconds": record.get("unmeasurableSeconds"),
    }})
    # The archive keeps what the export held, not the translation.
    night["garminResponse"] = without_arrays(record)
    return night


def stress_level(value: int | None) -> int | None:
    # A negative level marks a day with no measurable minute: -2 when every one was too active.
    return None if value is None or value < 0 else value


def daily_summary_from_export(record: dict[str, Any]) -> dict[str, Any] | None:
    if record.get("calendarDate") is None:
        return None
    body_battery = record.get("bodyBattery") or {}
    body_battery_stats = {stat.get("bodyBatteryStatType"): stat.get("statsValue")
                          for stat in body_battery.get("bodyBatteryStatList") or []}
    # The whole day's stress; the export also splits it into its awake and asleep shares.
    stress = next((aggregate for aggregate in (record.get("allDayStress") or {}).get("aggregatorList") or []
                   if aggregate.get("type") == "TOTAL"), {})
    summary = daily_summary_from_garmin(date.fromisoformat(record["calendarDate"]), {
        "calendarDate": record["calendarDate"],
        "includesWellnessData": record.get("includesWellnessData"),
        "includesActivityData": record.get("includesActivityData"),
        "bodyBatteryChargedValue": body_battery.get("chargedValue"),
        "bodyBatteryDrainedValue": body_battery.get("drainedValue"),
        "bodyBatteryHighestValue": body_battery_stats.get("HIGHEST"),
        "bodyBatteryLowestValue": body_battery_stats.get("LOWEST"),
        "averageStressLevel": stress_level(stress.get("averageStressLevel")),
        "maxStressLevel": stress_level(stress.get("maxStressLevel")),
        "restStressDuration": stress.get("restDuration"),
        "lowStressDuration": stress.get("lowDuration"),
        "mediumStressDuration": stress.get("mediumDuration"),
        "highStressDuration": stress.get("highDuration"),
    })
    if summary is not None:
        summary["garminResponse"] = without_arrays(record)
    return summary


def month_batches(
    activities: list[dict[str, Any]],
    sleep_nights: list[dict[str, Any]],
    daily_summaries: list[dict[str, Any]],
) -> list[dict[str, Any]]:
    """One version 3 batch per calendar month holding a record, oldest first."""
    dated = ([activity["startTimeUtc"][:10] for activity in activities]
             + [night["calendarDate"] for night in sleep_nights]
             + [summary["calendarDate"] for summary in daily_summaries])
    if not dated:
        return []
    # Version 3 states that every date in its range was synced, so no range reaches
    # before the archive's first record or past its last.
    first, last = min(dated), max(dated)
    batches = []
    for month in sorted({day[:7] for day in dated}):
        days_in_month = monthrange(int(month[:4]), int(month[5:]))[1]
        batches.append({
            "version": 3,
            "activityRange": {"fromDate": max(f"{month}-01", first),
                              "toDate": min(f"{month}-{days_in_month:02d}", last)},
            "activities": sorted((activity for activity in activities if activity["startTimeUtc"].startswith(month)),
                                 key=lambda activity: (activity["startTimeUtc"], activity["activityId"])),
            "sleepNights": sorted((night for night in sleep_nights if night["calendarDate"].startswith(month)),
                                  key=lambda night: night["calendarDate"]),
            "dailySummaries": sorted((summary for summary in daily_summaries
                                      if summary["calendarDate"].startswith(month)),
                                     key=lambda summary: summary["calendarDate"]),
        })
    return batches


def import_export_archive(archive: Path, post: Callable[[dict[str, Any]], Any]) -> tuple[int, int, int]:
    """Post the archive's history; returns (batches, records posted, records left out)."""
    # Keyed by identity, so a record the archive holds twice is posted once.
    activities: dict[int, dict[str, Any]] = {}
    sleep_nights: dict[str, dict[str, Any]] = {}
    daily_summaries: dict[str, dict[str, Any]] = {}
    left_out = 0
    for pattern, content in archive_files(archive, (ACTIVITY_FILES, SLEEP_FILES, DAILY_SUMMARY_FILES)):
        for item in json.loads(content):
            if pattern == ACTIVITY_FILES:
                for record in item.get("summarizedActivitiesExport") or []:
                    if activity := activity_from_export(record):
                        activities[activity["activityId"]] = activity
                    else:
                        left_out += 1
            elif pattern == SLEEP_FILES:
                if night := sleep_night_from_export(item):
                    sleep_nights[night["calendarDate"]] = night
                else:
                    left_out += 1
            elif summary := daily_summary_from_export(item):
                daily_summaries[summary["calendarDate"]] = summary
            else:
                left_out += 1
    batches = month_batches(list(activities.values()), list(sleep_nights.values()),
                            list(daily_summaries.values()))
    for batch in batches:
        post(batch)
    return len(batches), len(activities) + len(sleep_nights) + len(daily_summaries), left_out


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("archive", type=Path, help="the export zip, or the folder it was unpacked to")
    add_machine_arguments(parser)
    args = parser.parse_args()
    archive = args.archive.expanduser()
    if not archive.exists():
        parser.error(f"{archive} does not exist")
    token = ingest_token(parser, args)
    # The SyncSignIn rotates its saved refresh token, so the import takes the SyncSlot
    # rather than racing a scheduled sync for that file.
    try:
        with sync_slot(Path(args.slot_file).expanduser()):
            batches, posted, left_out = import_export_archive(
                archive, lambda batch: post_to_ingest(args.ingest_url, token, batch))
    except AlreadyRunning as held:
        print(f"{held}; wait for it to finish before importing.", file=sys.stderr)
        return 1
    print(f"Imported {posted} records in {batches} monthly batches; {left_out} records"
          " held nothing ingest can store and were left out")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
