"""Backfill history from Garmin's ExportArchive with no Garmin API calls: its
Activities, SleepNights and DailySummaries, posted to ingest as version 3 backfill
batches, one per calendar month. Run it against the zip Garmin sends or the folder it
was unpacked to. Ingest stores only what it does not already hold, so re-running it
changes nothing (ADR 0014). Given an ExportSetIndex written by OakShore.Coach.ExportFit,
each strength Activity is posted with the ExerciseSets its FIT file holds."""

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
from typing import Any, BinaryIO, Callable, Iterator, NamedTuple

from sync_activities import (activity_from_garmin, add_machine_arguments, daily_summary_from_garmin,
                             ingest_token, post_to_ingest, sleep_night_from_garmin, without_arrays)
from sync_slot import AlreadyRunning, sync_slot


SLEEP_STAGES = ("deepSleepSeconds", "lightSleepSeconds", "remSleepSeconds")
ACTIVITY_REQUIRED = ("activityId", "startTimeGmt", "activityType", "duration")


# Each export record is translated into the field names and units of Garmin's API, then
# built by the sync's own builder, so an Activity or day synced both ways posts the same.

def activity_from_export(record: dict[str, Any]) -> dict[str, Any] | None:
    """An Activity without its time in zones: the export's hrTimeInZone_N add zones 0 and 6
    and split time differently from the list's."""
    for field in ACTIVITY_REQUIRED:
        if record.get(field) is None:
            return None
    distance = record.get("distance")
    # The export counts epoch milliseconds, milliseconds and centimetres where the API's
    # list carries GMT text, seconds and metres.
    start = datetime.fromtimestamp(record["startTimeGmt"] / 1000, timezone.utc)
    return activity_from_garmin({
        "activityId": record["activityId"],
        "activityName": record.get("name"),
        "startTimeGMT": start.strftime("%Y-%m-%d %H:%M:%S"),
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
    start = record.get("sleepStartTimestampGMT")
    end = record.get("sleepEndTimestampGMT")
    if record.get("calendarDate") is None or start is None or end is None:
        return None
    # The export states no total. Garmin's sleep time is the deep, light and REM stages.
    stage_seconds = []
    for stage in SLEEP_STAGES:
        if record.get(stage) is not None:
            stage_seconds.append(record[stage])
    night = sleep_night_from_garmin(date.fromisoformat(record["calendarDate"]), {"dailySleepDTO": {
        "calendarDate": record["calendarDate"],
        "sleepStartTimestampGMT": epoch_ms(start),
        "sleepEndTimestampGMT": epoch_ms(end),
        "sleepTimeSeconds": sum(stage_seconds) if stage_seconds else None,
        "deepSleepSeconds": record.get("deepSleepSeconds"),
        "lightSleepSeconds": record.get("lightSleepSeconds"),
        "remSleepSeconds": record.get("remSleepSeconds"),
        "awakeSleepSeconds": record.get("awakeSleepSeconds"),
        "unmeasurableSleepSeconds": record.get("unmeasurableSeconds"),
    }})
    # The archive keeps what the export held, not the translation.
    night["garminResponse"] = without_arrays(record)
    return night


def measured(level: int | None) -> int | None:
    # A negative level marks a day with no measurable minute: -2 when every one was too active.
    if level is None or level < 0:
        return None
    return level


def daily_summary_from_export(record: dict[str, Any]) -> dict[str, Any] | None:
    if record.get("calendarDate") is None:
        return None
    body_battery = record.get("bodyBattery") or {}
    body_battery_stats = {}
    for stat in body_battery.get("bodyBatteryStatList") or []:
        body_battery_stats[stat.get("bodyBatteryStatType")] = stat.get("statsValue")
    # The whole day's StressLevel; the export also splits it into its awake and asleep shares.
    stress_level = {}
    for aggregate in (record.get("allDayStress") or {}).get("aggregatorList") or []:
        if aggregate.get("type") == "TOTAL":
            stress_level = aggregate
            break
    summary = daily_summary_from_garmin(date.fromisoformat(record["calendarDate"]), {
        "calendarDate": record["calendarDate"],
        "includesWellnessData": record.get("includesWellnessData"),
        "includesActivityData": record.get("includesActivityData"),
        "bodyBatteryChargedValue": body_battery.get("chargedValue"),
        "bodyBatteryDrainedValue": body_battery.get("drainedValue"),
        "bodyBatteryHighestValue": body_battery_stats.get("HIGHEST"),
        "bodyBatteryLowestValue": body_battery_stats.get("LOWEST"),
        "averageStressLevel": measured(stress_level.get("averageStressLevel")),
        "maxStressLevel": measured(stress_level.get("maxStressLevel")),
        "restStressDuration": stress_level.get("restDuration"),
        "lowStressDuration": stress_level.get("lowDuration"),
        "mediumStressDuration": stress_level.get("mediumDuration"),
        "highStressDuration": stress_level.get("highDuration"),
    })
    if summary is not None:
        summary["garminResponse"] = without_arrays(record)
    return summary


class ExportFileKind(NamedTuple):
    pattern: str
    build: Callable[[dict[str, Any]], dict[str, Any] | None]
    batch_list: str
    identity: str


# Files are chunked by date or by count and named after the account, so each kind is
# found by pattern and every chunk of it read. A record the archive holds twice shares
# its identity, so it is posted once.
EXPORT_FILE_KINDS = (
    ExportFileKind("*summarizedActivities*.json", activity_from_export, "activities", "activityId"),
    ExportFileKind("*sleepData*.json", sleep_night_from_export, "sleepNights", "calendarDate"),
    ExportFileKind("UDSFile*.json", daily_summary_from_export, "dailySummaries", "calendarDate"),
)


def kind_of(name: str) -> ExportFileKind | None:
    for kind in EXPORT_FILE_KINDS:
        if fnmatchcase(name, kind.pattern):
            return kind
    return None


def archive_files(archive: Path) -> Iterator[tuple[str, Callable[[], BinaryIO]]]:
    """Each file in the archive, wherever it lies, as its name and how to open it. Zips are
    opened at any depth and their files listed in their place."""
    if not archive.is_dir():
        yield from listed(archive.name, file_opener(archive))
        return
    for path in sorted(archive.rglob("*")):
        if path.is_file():
            yield from listed(path.name, file_opener(path))


def listed(name: str, open_file: Callable[[], BinaryIO]) -> Iterator[tuple[str, Callable[[], BinaryIO]]]:
    if not name.lower().endswith(".zip"):
        yield name, open_file
        return
    with open_file() as file, zipfile.ZipFile(file) as zipped:
        for info in sorted(zipped.infolist(), key=lambda member: member.filename):
            if not info.is_dir():
                yield from listed(PurePosixPath(info.filename).name, member_opener(zipped, info))


def file_opener(path: Path) -> Callable[[], BinaryIO]:
    return lambda: path.open("rb")


def member_opener(zipped: zipfile.ZipFile, info: zipfile.ZipInfo) -> Callable[[], BinaryIO]:
    # Read whole, because seeking back inside a compressed member restarts its decompression.
    return lambda: io.BytesIO(zipped.read(info))


def file_records(content: list[dict[str, Any]]) -> list[dict[str, Any]]:
    """The records one export file holds; Activity files wrap theirs in one more list."""
    records = []
    for item in content:
        if "summarizedActivitiesExport" in item:
            records.extend(item["summarizedActivitiesExport"] or [])
        else:
            records.append(item)
    return records


def with_exercise_sets(activity: dict[str, Any],
                       export_set_index: dict[str, list[dict[str, Any]]]) -> dict[str, Any]:
    """The Activity with the ExerciseSets the ExportSetIndex holds for its start time, if any.
    They must ride in the Activity's own backfill batch, because a later one naming an
    Activity already stored is dropped whole (ADR 0014)."""
    started = activity["startTimeUtc"]
    found = export_set_index.get(started)
    if found is None:
        found = same_minute(started, export_set_index)
    if found is None:
        return activity
    return {**activity, "exerciseSets": found}


def same_minute(started: str,
                export_set_index: dict[str, list[dict[str, Any]]]) -> list[dict[str, Any]] | None:
    """The sets of the one FIT file recorded in `started`'s minute, for the Activities the
    export dates to the whole minute while their FIT file states the second (ADR 0015).
    A minute holding more than one FIT file is left unmatched rather than guessed at."""
    if not started.endswith(":00Z"):
        return None
    minute = started[:-len(":00Z")]
    within = [sets for start, sets in export_set_index.items() if start.startswith(minute + ":")]
    return within[0] if len(within) == 1 else None


def month_of(record: dict[str, Any]) -> date:
    # A day is dated by its calendar date, an Activity by the UTC date it started.
    day = datetime.fromisoformat(record.get("calendarDate") or record["startTimeUtc"]).date()
    return day.replace(day=1)


def backfill_batch(month: date) -> dict[str, Any]:
    last_day = month.replace(day=monthrange(month.year, month.month)[1])
    return {
        "version": 3,
        "backfill": True,
        "activityRange": {"fromDate": month.isoformat(), "toDate": last_day.isoformat()},
        "activities": [],
        "sleepNights": [],
        "dailySummaries": [],
    }


def month_batches(records: dict[str, dict[Any, dict[str, Any]]]) -> list[dict[str, Any]]:
    """One backfill batch per calendar month holding a record, oldest first, each list in
    order of identity. `records` holds each batch list's records by identity."""
    batches: dict[date, dict[str, Any]] = {}
    for batch_list, by_identity in records.items():
        for identity in sorted(by_identity):
            record = by_identity[identity]
            month = month_of(record)
            if month not in batches:
                batches[month] = backfill_batch(month)
            batches[month][batch_list].append(record)
    ordered = []
    for month in sorted(batches):
        ordered.append(batches[month])
    return ordered


def import_export_archive(archive: Path, post: Callable[[dict[str, Any]], Any],
                          export_set_index: dict[str, list[dict[str, Any]]] | None = None) -> tuple[int, int, int]:
    """Post the archive's history; returns (batches, records posted, records left out)."""
    export_set_index = export_set_index or {}
    records: dict[str, dict[Any, dict[str, Any]]] = {}
    for kind in EXPORT_FILE_KINDS:
        records[kind.batch_list] = {}
    left_out = 0
    for name, open_file in archive_files(archive):
        kind = kind_of(name)
        if kind is None:
            continue
        with open_file() as file:
            content = json.load(file)
        for record in file_records(content):
            built = kind.build(record)
            if built is None:
                left_out += 1
            else:
                if kind.batch_list == "activities":
                    built = with_exercise_sets(built, export_set_index)
                records[kind.batch_list][built[kind.identity]] = built
    batches = month_batches(records)
    for batch in batches:
        post(batch)
    posted = 0
    for by_identity in records.values():
        posted += len(by_identity)
    return len(batches), posted, left_out


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("archive", type=Path, help="the export zip, or the folder it was unpacked to")
    parser.add_argument("--export-set-index", type=Path,
                        help="the ExportSetIndex OakShore.Coach.ExportFit wrote for this archive")
    add_machine_arguments(parser)
    args = parser.parse_args()
    archive = args.archive.expanduser()
    if not archive.exists():
        parser.error(f"{archive} does not exist")
    export_set_index = None
    if args.export_set_index is not None:
        index = args.export_set_index.expanduser()
        if not index.exists():
            parser.error(f"{index} does not exist")
        export_set_index = json.loads(index.read_text(encoding="utf-8"))
    token = ingest_token(parser, args)
    # The SyncSignIn rotates its saved refresh token, so the import takes the SyncSlot
    # rather than racing a scheduled sync for that file.
    try:
        with sync_slot(Path(args.slot_file).expanduser()):
            batches, posted, left_out = import_export_archive(
                archive, lambda batch: post_to_ingest(args.ingest_url, token, batch), export_set_index)
    except AlreadyRunning as held:
        print(f"{held}; wait for it to finish before importing.", file=sys.stderr)
        return 1
    print(f"Posted {posted} records in {batches} monthly backfill batches; {left_out} records"
          " held nothing ingest can store and were left out")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
