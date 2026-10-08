import json
import sys
import tempfile
import types
import unittest
from datetime import date, timedelta
from pathlib import Path
from unittest.mock import patch


ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "src" / "sync"))

from garmin_client import GarminAbort, GarminActivityClient, load_token_client  # noqa: E402
from sync_activities import run  # noqa: E402
from watermark import Watermark  # noqa: E402


FIXTURES = ROOT / "fixtures" / "garmin"
FIXTURE = (FIXTURES / "activitylist-service--search-activities__page-00.json").read_bytes()
LIST_ENTRIES = json.loads(FIXTURE)
STRENGTH_IDS = [entry["activityId"] for entry in LIST_ENTRIES
                if entry["activityType"]["typeKey"] == "strength_training"]
ZONES = (FIXTURES / "biometric-service--heartRateZones.json").read_bytes()
ZONES_URL = "https://garmin.example/biometric-service/heartRateZones"
START = date(2026, 8, 5)
END = date(2026, 9, 22)
DAYS = (END - START).days + 1
DAY = date(2026, 9, 21)
PROFILE = b'{"displayName": "00000000-0000-4000-8000-00000000000a"}'


def exercise_set_fixture(activity_id):
    return (FIXTURES / f"activity-service--exerciseSets__activity-{activity_id}.json").read_bytes()


def wellness(first):
    return [Response(200, PROFILE)] * first + [Response(204, b""), Response(204, b"")]


def corpus_responses():
    """Each day of the window, most recent first: its list response, the zone boundaries on the
    first day that needs them, one enrichment per strength Activity, then wellness."""
    results = []
    zones_fetched = False
    for offset in range(DAYS):
        day = (END - timedelta(days=offset)).isoformat()
        entries = [entry for entry in LIST_ENTRIES if entry["startTimeGMT"].startswith(day)]
        results.append(Response(200, json.dumps(entries).encode("utf-8")))
        if not zones_fetched and any(entry["activityType"]["typeKey"] != "strength_training"
                                     for entry in entries):
            results.append(Response(200, ZONES))
            zones_fetched = True
        results += [Response(200, exercise_set_fixture(entry["activityId"])) for entry in entries
                    if entry["activityType"]["typeKey"] == "strength_training"]
        results += wellness(offset == 0)
    return results


def sync_range(session, sleeps=None, start=START, end=END):
    """A dated run over the whole window, or one day of it, with a ceiling above its length."""
    posted = []
    with tempfile.TemporaryDirectory() as directory:
        summary = run(start, end, END, DAYS, Watermark(Path(directory, "watermark.json")),
                      client(session, [] if sleeps is None else sleeps), posted.append)
    return summary, posted


def posted_activities(posted):
    return [activity for batch in posted for activity in batch["activities"]]


class Response:
    def __init__(self, status_code, content=b"[]"):
        self.status_code = status_code
        self.content = content


class Session:
    def __init__(self, *results):
        self.results = list(results)
        self.calls = []

    def request(self, method, url, **kwargs):
        self.calls.append((method, url, kwargs))
        result = self.results.pop(0)
        if isinstance(result, BaseException):
            raise result
        return result


def client(session, sleeps):
    return GarminActivityClient(session, "https://garmin.example", lambda: {"Authorization": "Bearer fixture"},
                                sleep=sleeps.append)


class ActivitySyncTests(unittest.TestCase):
    def test_a_dated_run_replays_the_fixture_day_by_day_enriching_each_strength_activity_once(self):
        session = Session(*corpus_responses())
        sleeps = []

        summary, posted = sync_range(session, sleeps)

        # Per day a list call and two wellness calls; once per run the boundaries and the
        # profile; once per strength Activity its exercise sets.
        calls = 3 * DAYS + 2 + len(STRENGTH_IDS)
        self.assertEqual((DAYS, 15, 0), summary)
        self.assertEqual([4.0] * calls, sleeps)
        self.assertEqual(calls, len(session.calls))
        method, url, kwargs = session.calls[0]
        self.assertEqual("GET", method)
        self.assertEqual("https://garmin.example/activitylist-service/activities/search/activities", url)
        self.assertEqual({"startDate": "2026-09-22", "endDate": "2026-09-22",
                          "start": "0", "limit": "20"}, kwargs["params"])
        self.assertEqual(
            [f"https://garmin.example/activity-service/activity/{activity_id}/exerciseSets"
             for activity_id in STRENGTH_IDS],
            [url for _, url, _ in session.calls if url.endswith("/exerciseSets")])
        self.assertEqual(DAYS, len(posted))
        for offset, batch in enumerate(posted):
            day = (END - timedelta(days=offset)).isoformat()
            self.assertEqual(3, batch["version"])
            self.assertEqual({"fromDate": day, "toDate": day}, batch["activityRange"])
        activities = posted_activities(posted)
        self.assertEqual(15, len(activities))
        first = activities[0]
        self.assertEqual(24444892080, first["activityId"])
        self.assertEqual("strength_training", first["activityName"])
        self.assertEqual("2026-09-21T14:34:01Z", first["startTimeUtc"])
        self.assertEqual("strength_training", first["typeKey"])
        self.assertEqual(14, first["totalSets"])
        self.assertEqual(82, first["totalReps"])
        for activity in activities:
            if activity["activityId"] in STRENGTH_IDS:
                self.assertIn("exerciseSets", activity)
            else:
                self.assertNotIn("exerciseSets", activity)

    def test_exercise_sets_are_normalised_to_kilograms_with_no_grams_in_the_batch(self):
        session = Session(*corpus_responses())

        _, posted = sync_range(session)

        activities = {a["activityId"]: a for a in posted_activities(posted)}
        exercise_sets = activities[24444892080]["exerciseSets"]
        self.assertEqual(28, len(exercise_sets))
        self.assertEqual({
            "setType": "ACTIVE",
            "repetitions": 8,
            "weightKg": 0.0,
            "bodyweight": False,
            "exercise": {"category": "BANDED_EXERCISES", "name": "GLUTE_BRIDGE"},
            "candidateCount": 3,
            "topProbability": 99.609375,
            "startTimeUtc": "2026-09-21T14:34:01Z",
            "durationSeconds": 41.045,
            "wktStepIndex": 0,
        }, exercise_sets[0])
        bench = exercise_sets[5]
        self.assertEqual({"category": "BENCH_PRESS", "name": None}, bench["exercise"])
        self.assertEqual(75.0, bench["weightKg"])
        rest = exercise_sets[1]
        self.assertEqual("REST", rest["setType"])
        self.assertIsNone(rest["repetitions"])
        self.assertIsNone(rest["weightKg"])
        self.assertFalse(rest["bodyweight"])
        self.assertIsNone(rest["exercise"])
        self.assertEqual(0, rest["candidateCount"])
        self.assertIsNone(rest["topProbability"])
        cancelled = exercise_sets[6]
        self.assertEqual(("ACTIVE", 0, 85.0), (cancelled["setType"], cancelled["repetitions"],
                                               cancelled["weightKg"]))
        for activity in activities.values():
            for exercise_set in activity.get("exerciseSets", []):
                if exercise_set["weightKg"] is not None:
                    self.assertLess(exercise_set["weightKg"], 1000)

    def test_bodyweight_is_a_stated_load_and_connect_edited_rows_keep_their_nulls(self):
        session = Session(*corpus_responses())

        _, posted = sync_range(session)

        activities = {a["activityId"]: a for a in posted_activities(posted)}
        calf_raises = [s for s in activities[23898645459]["exerciseSets"]
                       if s["exercise"] == {"category": "CALF_RAISE", "name": "STANDING_CALF_RAISE"}]
        self.assertEqual([5, 10, 10], [s["repetitions"] for s in calf_raises])
        for calf_raise in calf_raises:
            self.assertTrue(calf_raise["bodyweight"])
            self.assertIsNone(calf_raise["weightKg"])
        edited = activities[23930958725]["exerciseSets"]
        self.assertTrue(all(s["wktStepIndex"] is None for s in edited))
        active = [s for s in edited if s["setType"] == "ACTIVE"]
        self.assertTrue(all(s["candidateCount"] == 1 and s["topProbability"] == 100.0
                            for s in active))
        rest = [s for s in edited if s["setType"] == "REST"]
        self.assertTrue(all(s["bodyweight"] and s["weightKg"] is None and s["startTimeUtc"] is None
                            for s in rest))

    def test_cardio_time_in_zones_comes_off_the_list_against_boundaries_fetched_once(self):
        session = Session(*corpus_responses())

        _, posted = sync_range(session)

        self.assertEqual([ZONES_URL], [url for _, url, _ in session.calls if "heartRateZones" in url])
        self.assertFalse(any("hrTimeInZones" in url for _, url, _ in session.calls))
        cardio_batches = [batch for batch in posted
                          if any(a["activityId"] not in STRENGTH_IDS for a in batch["activities"])]
        self.assertEqual(8, sum(a["activityId"] not in STRENGTH_IDS for a in posted_activities(posted)))
        for batch in posted:
            if batch not in cardio_batches:
                self.assertNotIn("sportProfiles", batch)
        for batch in cardio_batches:
            self.assertEqual([{
            "name": "DEFAULT",
            "trainingMethod": "HR_RESERVE",
            "restingHeartRateBpm": 51,
            "maxHeartRateBpm": 180,
            "lactateThresholdHeartRateBpm": None,
            "heartRateZones": [{"zoneNumber": 1, "lowBoundaryBpm": 116},
                               {"zoneNumber": 2, "lowBoundaryBpm": 128},
                               {"zoneNumber": 3, "lowBoundaryBpm": 141},
                               {"zoneNumber": 4, "lowBoundaryBpm": 154},
                               {"zoneNumber": 5, "lowBoundaryBpm": 167}],
            }], batch["sportProfiles"])
        for activity in posted_activities(posted):
            if activity["activityId"] in STRENGTH_IDS:
                self.assertNotIn("timeInHeartRateZones", activity)
                continue
            # The per-Activity endpoint's captured seconds equal the list entry's.
            captured = json.loads((FIXTURES / "activity-service--hrTimeInZones__activity-"
                                   f"{activity['activityId']}.json").read_bytes())
            self.assertEqual([{"zoneNumber": zone["zoneNumber"], "seconds": zone["secsInZone"]}
                              for zone in sorted(captured, key=lambda zone: zone["zoneNumber"])],
                             activity["timeInHeartRateZones"])

    def test_zone_fields_are_read_as_found_and_an_absent_zone_is_unknown_not_zero(self):
        entries = [
            {"activityId": 1, "activityName": None, "startTimeGMT": "2026-09-21 10:00:00",
             "activityType": {"typeKey": "running"}, "duration": 600.0,
             "hrTimeInZone_1": None, "hrTimeInZone_2": 120.5, "hrTimeInZone_6": 30.0},
            {"activityId": 2, "activityName": None, "startTimeGMT": "2026-09-21 12:00:00",
             "activityType": {"typeKey": "cycling"}, "duration": 600.0},
        ]
        zones = [{"sport": "DEFAULT", "trainingMethod": "PERCENT_MAX_HR", "zone1Floor": 100,
                  "zone2Floor": None, "zone3Floor": 140, "zone6Floor": 190},
                 {"sport": "CYCLING", "zone1Floor": 110}]
        session = Session(Response(200, json.dumps(entries).encode("utf-8")),
                          Response(200, json.dumps(zones).encode("utf-8")), *wellness(True))

        _, posted = sync_range(session, start=DAY, end=DAY)

        first, second = posted[0]["activities"]
        self.assertEqual([{"zoneNumber": 2, "seconds": 120.5}, {"zoneNumber": 6, "seconds": 30.0}],
                         first["timeInHeartRateZones"])
        self.assertNotIn("timeInHeartRateZones", second)
        default, cycling = posted[0]["sportProfiles"]
        self.assertEqual([{"zoneNumber": 1, "lowBoundaryBpm": 100}, {"zoneNumber": 3, "lowBoundaryBpm": 140},
                          {"zoneNumber": 6, "lowBoundaryBpm": 190}], default["heartRateZones"])
        self.assertEqual(("CYCLING", None), (cycling["name"], cycling["restingHeartRateBpm"]))

    def test_a_run_without_time_in_zones_makes_no_boundary_call(self):
        session = Session(Response(200, json.dumps([LIST_ENTRIES[0]]).encode("utf-8")),
                          Response(200, exercise_set_fixture(24444892080)), *wellness(True))

        _, posted = sync_range(session, start=DAY, end=DAY)

        self.assertFalse(any("heartRateZones" in url for _, url, _ in session.calls))
        self.assertNotIn("sportProfiles", posted[0])

    def test_an_unanticipated_sport_is_cardio_so_it_is_posted_but_never_enriched(self):
        entries = [{"activityId": 99, "activityName": None, "startTimeGMT": "2026-09-21 10:00:00",
                    "activityType": {"typeKey": "underwater_hockey"}, "duration": 600.0}]
        session = Session(Response(200, json.dumps(entries).encode("utf-8")), *wellness(True))

        (_, count, _), posted = sync_range(session, start=DAY, end=DAY)

        self.assertEqual(1, count)
        self.assertFalse(any("/activity-service/" in url or "heartRateZones" in url
                             for _, url, _ in session.calls))
        activity = posted[0]["activities"][0]
        self.assertEqual("underwater_hockey", activity["typeKey"])
        self.assertNotIn("exerciseSets", activity)

    def test_enrichment_failures_abort_or_retry_like_any_other_garmin_call(self):
        strength_list = Response(200, json.dumps([LIST_ENTRIES[0]]).encode("utf-8"))
        with self.subTest("429 aborts without posting"):
            session = Session(strength_list, Response(429))
            with self.assertRaises(GarminAbort):
                sync_range(session, start=DAY, end=DAY)
            self.assertEqual(2, len(session.calls))
        with self.subTest("5xx retries with backoff"):
            session = Session(strength_list, Response(503),
                              Response(200, exercise_set_fixture(24444892080)), *wellness(True))
            sleeps = []
            _, posted = sync_range(session, sleeps, start=DAY, end=DAY)
            self.assertEqual([4.0, 4.0, 1, 4.0] + [4.0] * 3, sleeps)
            self.assertEqual(28, len(posted[0]["activities"][0]["exerciseSets"]))
        with self.subTest("a response in an unexpected shape stops the run"):
            session = Session(strength_list, Response(200, b"[]"))
            with self.assertRaisesRegex(RuntimeError, "exercise sets"):
                sync_range(session, start=DAY, end=DAY)
        cardio_list = Response(200, json.dumps([LIST_ENTRIES[1]]).encode("utf-8"))
        with self.subTest("429 on the boundaries aborts without posting"):
            session = Session(cardio_list, Response(429))
            with self.assertRaises(GarminAbort):
                sync_range(session, start=DAY, end=DAY)
            self.assertEqual(2, len(session.calls))
        for zones in ([], [{"sport": "RUNNING", "zone1Floor": 120}],
                      [{"sport": "DEFAULT", "zone1Floor": None}], [{"zone1Floor": 120}]):
            with self.subTest("boundaries without a usable DEFAULT stop the run", zones=zones):
                session = Session(Response(200, json.dumps([LIST_ENTRIES[1]]).encode("utf-8")),
                                  Response(200, json.dumps(zones).encode("utf-8")))
                with self.assertRaisesRegex(RuntimeError, "DEFAULT"):
                    sync_range(session, start=DAY, end=DAY)
                self.assertEqual(2, len(session.calls))
        with self.subTest("boundaries in an unexpected shape stop the run"):
            session = Session(Response(200, json.dumps([LIST_ENTRIES[1]]).encode("utf-8")),
                              Response(200, b"{}"))
            with self.assertRaisesRegex(RuntimeError, "heart rate zones"):
                sync_range(session, start=DAY, end=DAY)

    def test_an_empty_day_still_posts_a_batch(self):
        _, posted = sync_range(Session(Response(200), *wellness(True)), start=DAY, end=DAY)
        self.assertEqual([], posted[0]["activities"])

    def test_auth_challenge_and_rate_limit_abort_without_retry_or_post(self):
        for status in (401, 403, 429):
            with self.subTest(status=status):
                session = Session(Response(status))
                with self.assertRaises(GarminAbort) as raised:
                    sync_range(session)
                self.assertEqual(status, raised.exception.status)
                self.assertEqual(1, len(session.calls))

    def test_network_and_server_failures_retry_with_backoff(self):
        for failure in (OSError("connection lost"), Response(503)):
            with self.subTest(failure=failure):
                session = Session(failure, *corpus_responses())
                sleeps = []
                _, posted = sync_range(session, sleeps)
                calls = 3 * DAYS + 2 + len(STRENGTH_IDS)
                self.assertEqual([4.0, 1] + [4.0] * calls, sleeps)
                self.assertEqual(calls + 1, len(session.calls))
                self.assertEqual(15, len(posted_activities(posted)))

    def test_saved_token_is_loaded_without_login_and_refresh_is_paced(self):
        with tempfile.TemporaryDirectory() as directory:
            Path(directory, "garmin_tokens.json").write_text("fixture", encoding="utf-8")
            calls = []

            class Inner:
                is_authenticated = True

                def load(self, path):
                    calls.append(("load", path))

                def _token_expires_soon(self):
                    return True

                def _refresh_di_token(self):
                    calls.append(("refresh",))

                def dump(self, path):
                    calls.append(("dump", path))

            outer = types.SimpleNamespace(client=Inner())
            package = types.ModuleType("garminconnect")
            package.Garmin = lambda: outer
            submodule = types.ModuleType("garminconnect.client")
            submodule.token_file_path = lambda path: Path(path) / "garmin_tokens.json"
            sleeps = []
            with patch.dict(sys.modules, {"garminconnect": package, "garminconnect.client": submodule}):
                self.assertIs(outer, load_token_client(directory, sleep=sleeps.append))
            self.assertEqual([4.0], sleeps)
            self.assertEqual([("load", directory), ("refresh",), ("dump", directory)], calls)

    def test_token_refresh_retries_server_errors_but_not_rate_limits(self):
        with tempfile.TemporaryDirectory() as directory:
            Path(directory, "garmin_tokens.json").write_text("fixture", encoding="utf-8")
            package = types.ModuleType("garminconnect")
            submodule = types.ModuleType("garminconnect.client")
            submodule.token_file_path = lambda path: Path(path) / "garmin_tokens.json"
            for status, expected_calls, expected_sleeps in ((503, 2, [4.0, 1, 4.0]),
                                                              (429, 1, [4.0])):
                with self.subTest(status=status):
                    class Inner:
                        is_authenticated = True

                        def __init__(self):
                            self.calls = 0

                        def load(self, path):
                            pass

                        def _token_expires_soon(self):
                            return True

                        def _refresh_di_token(self):
                            self.calls += 1
                            if self.calls == 1:
                                raise RuntimeError(f"DI token refresh failed: {status}")

                        def dump(self, path):
                            pass

                    inner = Inner()
                    package.Garmin = lambda: types.SimpleNamespace(client=inner)
                    sleeps = []
                    with patch.dict(sys.modules, {"garminconnect": package, "garminconnect.client": submodule}):
                        if status == 429:
                            with self.assertRaisesRegex(RuntimeError, "429"):
                                load_token_client(directory, sleep=sleeps.append)
                        else:
                            load_token_client(directory, sleep=sleeps.append)
                    self.assertEqual(expected_calls, inner.calls)
                    self.assertEqual(expected_sleeps, sleeps)


if __name__ == "__main__":
    unittest.main()
