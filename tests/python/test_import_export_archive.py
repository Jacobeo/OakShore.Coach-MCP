import io
import json
import shutil
import subprocess
import sys
import tempfile
import unittest
import zipfile
from pathlib import Path, PurePosixPath


ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "src" / "sync"))

from import_export_archive import import_export_archive  # noqa: E402
from sync_activities import activity_from_garmin  # noqa: E402


ARCHIVE = ROOT / "tests" / "python" / "fixtures" / "garmin-export" / "archive"
LIST_ENTRIES = json.loads((ROOT / "fixtures" / "garmin" / "activitylist-service--search-activities__page-00.json")
                          .read_text(encoding="utf-8"))


def imported(archive=ARCHIVE):
    posted = []
    import_export_archive(archive, posted.append)
    return posted


def posted_activities(posted):
    return {activity["activityId"]: activity for batch in posted for activity in batch["activities"]}


class ExportArchiveImportTests(unittest.TestCase):
    def test_an_activity_also_synced_from_the_api_is_posted_with_the_identity_and_units_the_sync_posts(self):
        activities = posted_activities(imported())

        # One strength, one running and one indoor cycling Activity sit in both corpora.
        for activity_id in (24272601315, 24313465171, 24174672446):
            api_entry = next(entry for entry in LIST_ENTRIES if entry["activityId"] == activity_id)
            self.assertEqual(activity_from_garmin(api_entry), activities[activity_id])

    def test_each_month_holding_a_record_is_one_version_3_batch_bounded_by_what_the_archive_covers(self):
        posted = imported()

        self.assertEqual([3] * 5, [batch["version"] for batch in posted])
        # A version 3 range states every date in it was synced, so it never reaches before the
        # archive's first record or past its last.
        self.assertEqual([
            ("2024-06-29", "2024-06-30", [16155825735], [], []),
            ("2024-08-01", "2024-08-31", [16867435216], [], []),
            ("2026-06-01", "2026-06-30", [], ["2026-06-08", "2026-06-09"], ["2026-06-08", "2026-06-09"]),
            ("2026-08-01", "2026-08-31", [24174672446], [], []),
            ("2026-09-01", "2026-09-16", [24272601315, 24313465171],
             ["2026-09-15", "2026-09-16"], ["2026-09-15", "2026-09-16"]),
        ], [(batch["activityRange"]["fromDate"], batch["activityRange"]["toDate"],
             [activity["activityId"] for activity in batch["activities"]],
             [night["calendarDate"] for night in batch["sleepNights"]],
             [summary["calendarDate"] for summary in batch["dailySummaries"]]) for batch in posted])

    def test_an_activity_in_any_chunk_of_the_activity_files_is_posted_in_seconds_and_metres(self):
        activities = posted_activities(imported())

        self.assertEqual({
            "activityId": 16155825735,
            "activityName": "walking",
            "startTimeUtc": "2024-06-29T11:41:13Z",
            "typeKey": "walking",
            "durationSeconds": 1486.427001953125,
            "distanceMeters": 494.0899963378906,
            "totalSets": None,
            "activeSets": None,
            "totalReps": None,
        }, activities[16155825735])
        self.assertIsNone(activities[16867435216]["distanceMeters"])

    def test_the_zip_garmin_sends_imports_as_its_unpacked_folder_does_with_nested_zips_opened(self):
        sleep_chunk = "DI_CONNECT/DI-Connect-Wellness/2026-03-02_2026-06-10_1_sleepData.json"
        nested = io.BytesIO()
        with zipfile.ZipFile(nested, "w") as inner:
            inner.write(ARCHIVE / sleep_chunk, PurePosixPath(sleep_chunk).name)
        with tempfile.TemporaryDirectory() as directory:
            archive = Path(directory, "export.zip")
            with zipfile.ZipFile(archive, "w", zipfile.ZIP_DEFLATED) as outer:
                for path in sorted(ARCHIVE.rglob("*")):
                    name = path.relative_to(ARCHIVE).as_posix()
                    if path.is_file() and name != sleep_chunk:
                        outer.write(path, name)
                outer.writestr("DI_CONNECT/DI-Connect-Wellness/moved.zip", nested.getvalue())

            self.assertEqual(imported(), imported(archive))

    def test_reimporting_the_same_archive_posts_identical_batches(self):
        self.assertEqual(imported(), imported())

    def test_a_record_the_archive_holds_twice_is_posted_once(self):
        with tempfile.TemporaryDirectory() as directory:
            archive = Path(directory, "archive")
            shutil.copytree(ARCHIVE, archive)
            for pattern in ("*_1_summarizedActivities.json", "*_sleepData.json", "UDSFile_*.json"):
                for path in archive.rglob(pattern):
                    shutil.copy(path, path.with_name("copy-" + path.name))

            self.assertEqual(imported(), imported(archive))

    def test_a_trimmed_archive_is_imported_with_what_it_holds_and_its_missing_figures_unknown(self):
        def trim(records, *fields):
            return [{key: value for key, value in record.items() if key not in fields} for record in records]

        with tempfile.TemporaryDirectory() as directory:
            archive = Path(directory, "archive")
            shutil.copytree(ARCHIVE, archive)
            for path in archive.rglob("UDSFile_2026-06-10_*.json"):
                path.unlink()
            activities = next(archive.rglob("*_301_summarizedActivities.json"))
            chunks = json.loads(activities.read_text(encoding="utf-8"))
            chunks[0]["summarizedActivitiesExport"] = trim(chunks[0]["summarizedActivitiesExport"], "name", "distance")
            activities.write_text(json.dumps(chunks), encoding="utf-8")
            sleep = next(archive.rglob("2026-06-10_*_sleepData.json"))
            sleep.write_text(json.dumps(trim(json.loads(sleep.read_text(encoding="utf-8")),
                                             "remSleepSeconds", "spo2SleepSummary")), encoding="utf-8")
            summaries = next(archive.rglob("UDSFile_2026-03-02_*.json"))
            summaries.write_text(json.dumps(trim(json.loads(summaries.read_text(encoding="utf-8")),
                                                 "bodyBattery")), encoding="utf-8")

            posted = imported(archive)

        walk = posted_activities(posted)[16155825735]
        self.assertEqual((None, None, 1486.427001953125),
                         (walk["activityName"], walk["distanceMeters"], walk["durationSeconds"]))
        nights = {night["calendarDate"]: night for batch in posted for night in batch["sleepNights"]}
        # Asleep is the stages the night states; REM the export left out is unknown, never zero.
        self.assertEqual((318, None), (nights["2026-09-15"]["sleepMinutes"], nights["2026-09-15"]["remSleepMinutes"]))
        summaries = {summary["calendarDate"]: summary for batch in posted for summary in batch["dailySummaries"]}
        self.assertEqual(["2026-06-08", "2026-06-09"], sorted(summaries))
        self.assertEqual({"charged": None, "drained": None, "highest": None, "lowest": None},
                         summaries["2026-06-08"]["bodyBattery"])
        self.assertEqual(26, summaries["2026-06-08"]["stressLevel"]["average"])

    def test_a_day_with_no_measurable_stress_posts_its_stress_level_unknown_rather_than_negative(self):
        with tempfile.TemporaryDirectory() as directory:
            archive = Path(directory, "archive")
            shutil.copytree(ARCHIVE, archive)
            summaries = next(archive.rglob("UDSFile_2026-03-02_*.json"))
            records = json.loads(summaries.read_text(encoding="utf-8"))
            # As the real export's first day reads: one minute, too active to measure.
            records[0]["allDayStress"]["aggregatorList"][0].update(averageStressLevel=-2, maxStressLevel=-2)
            summaries.write_text(json.dumps(records), encoding="utf-8")

            posted = imported(archive)

        summary = next(summary for batch in posted for summary in batch["dailySummaries"]
                       if summary["calendarDate"] == "2026-06-08")
        self.assertEqual((None, None, 52560), (summary["stressLevel"]["average"], summary["stressLevel"]["maximum"],
                                               summary["stressLevel"]["restSeconds"]))

    def test_a_record_ingest_could_not_store_is_left_out_and_counted_while_the_rest_import(self):
        with tempfile.TemporaryDirectory() as directory:
            archive = Path(directory, "archive")
            shutil.copytree(ARCHIVE, archive)
            activities = next(archive.rglob("*_301_summarizedActivities.json"))
            chunks = json.loads(activities.read_text(encoding="utf-8"))
            del chunks[0]["summarizedActivitiesExport"][0]["duration"]
            activities.write_text(json.dumps(chunks), encoding="utf-8")
            posted = []

            summary = import_export_archive(archive, posted.append)

        self.assertEqual((5, 13, 0), import_export_archive(ARCHIVE, [].append))
        self.assertEqual((4, 12, 1), summary)
        self.assertNotIn(16155825735, posted_activities(posted))
        self.assertEqual("2024-08-26", posted[0]["activityRange"]["fromDate"])

    def test_the_importer_never_loads_the_garmin_client(self):
        script = (f"import sys; sys.path.insert(0, {str(ROOT / 'src' / 'sync')!r});"
                  "import import_export_archive as importer;"
                  f"importer.import_export_archive(importer.Path({str(ARCHIVE)!r}), [].append);"
                  "print(sorted(name for name in sys.modules if name.startswith('garmin')))")

        loaded = subprocess.run([sys.executable, "-I", "-c", script], capture_output=True, text=True, check=True)

        self.assertEqual("[]", loaded.stdout.strip())

    def test_an_archive_lacking_any_one_file_or_any_one_field_is_still_imported(self):
        def field_names(value):
            if isinstance(value, dict):
                return set(value).union(*map(field_names, value.values()))
            if isinstance(value, list):
                return set().union(*map(field_names, value))
            return set()

        def without(value, field):
            if isinstance(value, dict):
                return {key: without(item, field) for key, item in value.items() if key != field}
            if isinstance(value, list):
                return [without(item, field) for item in value]
            return value

        with tempfile.TemporaryDirectory() as directory:
            archive = Path(directory, "archive")
            shutil.copytree(ARCHIVE, archive)
            files = sorted(path for path in archive.rglob("*") if path.is_file())
            for path in files:
                original = path.read_bytes()
                path.unlink()
                with self.subTest(missing=path.name):
                    self.assertTrue(imported(archive))
                path.write_bytes(original)
            contents = {path: json.loads(path.read_text(encoding="utf-8")) for path in files if path.suffix == ".json"}
            fields = {path: field_names(content) for path, content in contents.items()}
            # Removed from every record at every depth, as an export from another version
            # of Garmin's exporter could omit it.
            for field in sorted(set().union(*fields.values())):
                holding = [path for path in contents if field in fields[path]]
                for path in holding:
                    path.write_text(json.dumps(without(contents[path], field)), encoding="utf-8")
                with self.subTest(missing=field):
                    self.assertTrue(imported(archive))
                for path in holding:
                    path.write_text(json.dumps(contents[path]), encoding="utf-8")


class ExportArchiveWellnessTests(unittest.TestCase):
    def test_every_date_chunked_sleep_file_is_read_and_its_nights_are_posted_in_minutes(self):
        sleep_nights = {night["calendarDate"]: night for batch in imported() for night in batch["sleepNights"]}

        self.assertEqual(["2026-06-08", "2026-06-09", "2026-09-15", "2026-09-16"], sorted(sleep_nights))
        night = sleep_nights["2026-09-15"]
        self.assertEqual({
            "calendarDate": "2026-09-15",
            "startTimeUtc": "2026-09-14T19:34:00Z",
            "endTimeUtc": "2026-09-15T04:07:00Z",
            # The export states no total; asleep is deep, light and REM, as Garmin's sleepTimeSeconds is.
            "sleepMinutes": 496,
            "deepSleepMinutes": 33,
            "lightSleepMinutes": 285,
            "remSleepMinutes": 178,
            "awakeSleepMinutes": 17,
            "unmeasurableSleepMinutes": 0,
        }, {key: value for key, value in night.items() if key != "garminResponse"})
        self.assertEqual(91.74, night["garminResponse"]["spo2SleepSummary"]["averageSPO2"])

    def test_every_date_chunked_daily_rollup_is_read_as_the_days_daily_summary(self):
        daily_summaries = {summary["calendarDate"]: summary
                           for batch in imported() for summary in batch["dailySummaries"]}

        self.assertEqual(["2026-06-08", "2026-06-09", "2026-09-15", "2026-09-16"], sorted(daily_summaries))
        summary = daily_summaries["2026-06-08"]
        self.assertEqual({"charged": 76, "drained": 43, "highest": 64, "lowest": 5}, summary["bodyBattery"])
        # The whole day's figures, not the awake or asleep share of it.
        self.assertEqual({"average": 26, "maximum": 96, "restSeconds": 52560, "lowSeconds": 14760,
                          "mediumSeconds": 5880, "highSeconds": 2880}, summary["stressLevel"])
        archived = summary["garminResponse"]
        self.assertEqual(4018, archived["totalSteps"])
        self.assertNotIn("aggregatorList", archived["allDayStress"])


if __name__ == "__main__":
    unittest.main()
