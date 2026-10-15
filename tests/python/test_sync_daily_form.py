import json
import sys
import tempfile
import unittest
from datetime import date
from pathlib import Path


ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "src" / "sync"))

from garmin_client import GarminActivityClient  # noqa: E402
from sync_activities import run_watermark  # noqa: E402
from watermark import Watermark  # noqa: E402


FIXTURES = ROOT / "fixtures" / "garmin"
DISPLAY_NAME = "00000000-0000-4000-8000-00000000000a"
PROFILE = json.dumps({"displayName": DISPLAY_NAME}).encode("utf-8")
GARMIN = "https://garmin.example"


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
        return self.results.pop(0)


def training_status_payload(day):
    return json.loads((FIXTURES / f"metrics-service--trainingstatus-aggregated__date-{day}.json")
                      .read_text(encoding="utf-8"))


def training_status(day, payload=None):
    return Response(200, json.dumps(training_status_payload(day) if payload is None else payload).encode("utf-8"))


def no_wellness():
    return [Response(204, b""), Response(204, b"")]


class DailyFormSyncTests(unittest.TestCase):
    def setUp(self):
        directory = tempfile.TemporaryDirectory()
        self.addCleanup(directory.cleanup)
        self.path = Path(directory.name, "watermark.json")

    def sync(self, today, earliest, *results):
        self.session = Session(*results)
        self.sleeps = []
        self.posted = posted = []
        garmin = GarminActivityClient(self.session, GARMIN, lambda: {"Authorization": "Bearer fixture"},
                                      sleep=self.sleeps.append)
        return run_watermark(today, earliest, 30, Watermark(self.path), garmin, posted.append)

    def requests(self):
        return [(url.removeprefix(GARMIN), kwargs["params"]) for _, url, kwargs in self.session.calls]

    def test_a_day_costs_four_calls_with_training_status_last_and_posts_a_version_4_batch(self):
        self.sync(date(2026, 9, 20), date(2026, 9, 19),
                  Response(200), Response(200, PROFILE), *no_wellness(), training_status("2026-09-20"),
                  Response(200), *no_wellness(), training_status("2026-09-19"))

        day_list = "/activitylist-service/activities/search/activities"
        self.assertEqual([
            (day_list, {"startDate": "2026-09-20", "endDate": "2026-09-20", "start": "0", "limit": "20"}),
            ("/userprofile-service/socialProfile", {}),
            (f"/wellness-service/wellness/dailySleepData/{DISPLAY_NAME}",
             {"date": "2026-09-20", "nonSleepBufferMinutes": "60"}),
            (f"/usersummary-service/usersummary/daily/{DISPLAY_NAME}", {"calendarDate": "2026-09-20"}),
            ("/metrics-service/metrics/trainingstatus/aggregated/2026-09-20", {}),
            (day_list, {"startDate": "2026-09-19", "endDate": "2026-09-19", "start": "0", "limit": "20"}),
            (f"/wellness-service/wellness/dailySleepData/{DISPLAY_NAME}",
             {"date": "2026-09-19", "nonSleepBufferMinutes": "60"}),
            (f"/usersummary-service/usersummary/daily/{DISPLAY_NAME}", {"calendarDate": "2026-09-19"}),
            ("/metrics-service/metrics/trainingstatus/aggregated/2026-09-19", {}),
        ], self.requests())
        self.assertEqual([4.0] * 9, self.sleeps)
        self.assertEqual([4, 4], [batch["version"] for batch in self.posted])
        training_load = dict(self.posted[0]["trainingLoads"][0])
        archived = training_load.pop("garminResponse")
        # Verbatim from the payload; Garmin's codes stay only in the archive, arrays removed (ADR 0010).
        self.assertEqual({"calendarDate": "2026-09-20", "weeklyTrainingLoad": 135,
                          "loadTunnelMin": 67, "loadTunnelMax": 218}, training_load)
        status = archived["mostRecentTrainingStatus"]
        self.assertNotIn("recordedDevices", status)
        self.assertEqual((0, 2, 0), tuple(status["latestTrainingStatusData"]["1"][code]
                                          for code in ("trainingStatus", "loadLevelTrend", "fitnessTrend")))
        # Each estimate keeps the date Garmin computed it, before the day it was read on.
        self.assertEqual([{"sport": "generic", "calendarDate": "2026-09-02", "mlPerKgMin": 39.3},
                          {"sport": "cycling", "calendarDate": "2026-08-30", "mlPerKgMin": 32.0}],
                         self.posted[0]["vo2maxEstimates"])
        self.assertEqual(self.posted[0]["vo2maxEstimates"], self.posted[1]["vo2maxEstimates"])

    def test_a_day_garmin_has_no_training_status_for_is_posted_empty_and_completed(self):
        today, empty_day = date(2010, 1, 2), date(2010, 1, 1)

        self.sync(today, empty_day,
                  Response(200), Response(200, PROFILE), *no_wellness(), Response(204, b""),
                  Response(200), *no_wellness(), training_status("2010-01-01"))

        self.assertEqual([([], []), ([], [])],
                         [(batch["trainingLoads"], batch["vo2maxEstimates"]) for batch in self.posted])
        self.assertEqual([today], Watermark(self.path).pending_days(empty_day, today))

    def test_a_status_garmin_dated_before_the_day_is_not_that_days_training_load(self):
        self.sync(date(2026, 9, 22), date(2026, 9, 22),
                  Response(200), Response(200, PROFILE), *no_wellness(), training_status("2026-09-21"))

        self.assertEqual([], self.posted[0]["trainingLoads"])
        self.assertEqual(["generic", "cycling"], [estimate["sport"] for estimate in self.posted[0]["vo2maxEstimates"]])

    def test_a_status_dated_after_the_day_requested_stops_the_run(self):
        with self.assertRaisesRegex(RuntimeError, "2026-09-21"):
            self.sync(date(2026, 9, 20), date(2026, 9, 20),
                      Response(200), Response(200, PROFILE), *no_wellness(), training_status("2026-09-21"))

        self.assertEqual([], self.posted)

    def test_a_status_from_more_than_one_device_stops_the_run_rather_than_choosing_a_watch(self):
        payload = training_status_payload("2026-09-21")
        by_device = payload["mostRecentTrainingStatus"]["latestTrainingStatusData"]
        by_device["2"] = {**by_device["1"], "deviceId": 2, "weeklyTrainingLoad": 300}

        with self.assertRaisesRegex(RuntimeError, "2 devices"):
            self.sync(date(2026, 9, 21), date(2026, 9, 21),
                      Response(200), Response(200, PROFILE), *no_wellness(), training_status("2026-09-21", payload))

        self.assertEqual([], self.posted)
        self.assertEqual([date(2026, 9, 21)], Watermark(self.path).pending_days(date(2026, 9, 21), date(2026, 9, 21)))

    def test_a_status_carrying_no_load_figures_is_no_training_load_rather_than_one_of_nulls(self):
        payload = training_status_payload("2026-09-21")
        status = payload["mostRecentTrainingStatus"]["latestTrainingStatusData"]["1"]
        status.update(weeklyTrainingLoad=None, loadTunnelMin=None, loadTunnelMax=None)

        self.sync(date(2026, 9, 21), date(2026, 9, 21),
                  Response(200), Response(200, PROFILE), *no_wellness(), training_status("2026-09-21", payload))

        self.assertEqual([], self.posted[0]["trainingLoads"])

    def test_a_day_missing_vo2max_still_posts_its_training_load_and_no_estimate_in_its_place(self):
        payload = training_status_payload("2026-09-21")
        payload["mostRecentVO2Max"]["generic"] = None
        payload["mostRecentVO2Max"]["cycling"]["vo2MaxPreciseValue"] = None

        self.sync(date(2026, 9, 21), date(2026, 9, 21),
                  Response(200), Response(200, PROFILE), *no_wellness(), training_status("2026-09-21", payload))

        self.assertEqual([140], [load["weeklyTrainingLoad"] for load in self.posted[0]["trainingLoads"]])
        self.assertEqual([], self.posted[0]["vo2maxEstimates"])

    def test_a_vo2max_dated_after_the_day_read_stops_the_run(self):
        payload = training_status_payload("2026-09-21")
        payload["mostRecentVO2Max"]["cycling"]["calendarDate"] = "2026-09-22"

        with self.assertRaisesRegex(RuntimeError, "cycling VO2max"):
            self.sync(date(2026, 9, 21), date(2026, 9, 21),
                      Response(200), Response(200, PROFILE), *no_wellness(), training_status("2026-09-21", payload))

        self.assertEqual([], self.posted)


if __name__ == "__main__":
    unittest.main()
