import json
import sys
import tempfile
import unittest
from datetime import date
from pathlib import Path


ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "src" / "sync"))

from garmin_client import GarminAbort, GarminActivityClient  # noqa: E402
from sync_activities import run_watermark  # noqa: E402
from watermark import Watermark  # noqa: E402


FIXTURES = ROOT / "fixtures" / "garmin"
LIST_ENTRIES = json.loads((FIXTURES / "activitylist-service--search-activities__page-00.json")
                          .read_text(encoding="utf-8"))
# The corpus placeholder. The social profile itself is never committed, so its
# response is the one built here rather than replayed.
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
        result = self.results.pop(0)
        if isinstance(result, BaseException):
            raise result
        return result


def fixture(name):
    return Response(200, (FIXTURES / name).read_bytes())


def sleep_night(day):
    return fixture(f"wellness-service--dailySleepData__date-{day}.json")


def daily_summary(day):
    return fixture(f"usersummary-service--daily__date-{day}.json")


def training_status(day):
    return fixture(f"metrics-service--trainingstatus-aggregated__date-{day}.json")


def activities(day):
    entries = [entry for entry in LIST_ENTRIES if entry["startTimeGMT"].startswith(day)]
    results = [Response(200, json.dumps(entries).encode("utf-8"))]
    for entry in entries:
        if entry["activityType"]["typeKey"] == "strength_training":
            results.append(fixture(f"activity-service--exerciseSets__activity-{entry['activityId']}.json"))
    return results


def no_lists(value):
    if isinstance(value, dict):
        return all(not isinstance(item, list) and no_lists(item) for item in value.values())
    return True


class WellnessSyncTests(unittest.TestCase):
    def setUp(self):
        directory = tempfile.TemporaryDirectory()
        self.addCleanup(directory.cleanup)
        self.path = Path(directory.name, "watermark.json")

    def sync(self, today, earliest, max_days, *results):
        self.session = Session(*results)
        self.sleeps = []
        # Kept on the test so an aborted run's posts can still be asserted.
        self.posted = posted = []
        garmin = GarminActivityClient(self.session, GARMIN, lambda: {"Authorization": "Bearer fixture"},
                                      sleep=self.sleeps.append)
        summary = run_watermark(today, earliest, max_days, Watermark(self.path), garmin, posted.append)
        return summary, posted

    def requests(self):
        return [(url.removeprefix(GARMIN), kwargs["params"]) for _, url, kwargs in self.session.calls]

    def test_each_day_fetches_sleep_and_the_daily_summary_after_its_activities_paced_within_the_ceiling(self):
        summary, _ = self.sync(date(2026, 9, 21), date(2026, 9, 19), 2,
                               *activities("2026-09-21"), Response(200, PROFILE),
                               sleep_night("2026-09-21"), daily_summary("2026-09-21"), training_status("2026-09-21"),
                               *activities("2026-09-20"), sleep_night("2026-09-20"), daily_summary("2026-09-20"),
                               training_status("2026-09-20"))

        self.assertEqual((2, 1, 1), summary)
        day_list = "/activitylist-service/activities/search/activities"
        self.assertEqual([
            (day_list, {"startDate": "2026-09-21", "endDate": "2026-09-21", "start": "0", "limit": "20"}),
            ("/activity-service/activity/24444892080/exerciseSets", {}),
            ("/userprofile-service/socialProfile", {}),
            (f"/wellness-service/wellness/dailySleepData/{DISPLAY_NAME}",
             {"date": "2026-09-21", "nonSleepBufferMinutes": "60"}),
            (f"/usersummary-service/usersummary/daily/{DISPLAY_NAME}", {"calendarDate": "2026-09-21"}),
            ("/metrics-service/metrics/trainingstatus/aggregated/2026-09-21", {}),
            (day_list, {"startDate": "2026-09-20", "endDate": "2026-09-20", "start": "0", "limit": "20"}),
            (f"/wellness-service/wellness/dailySleepData/{DISPLAY_NAME}",
             {"date": "2026-09-20", "nonSleepBufferMinutes": "60"}),
            (f"/usersummary-service/usersummary/daily/{DISPLAY_NAME}", {"calendarDate": "2026-09-20"}),
            ("/metrics-service/metrics/trainingstatus/aggregated/2026-09-20", {}),
        ], self.requests())
        self.assertEqual([4.0] * 10, self.sleeps)

    def test_a_day_commits_its_activities_sleep_night_and_daily_summary_as_one_batch(self):
        _, posted = self.sync(date(2026, 9, 21), date(2026, 9, 21), 1,
                              *activities("2026-09-21"), Response(200, PROFILE),
                              sleep_night("2026-09-21"), daily_summary("2026-09-21"), training_status("2026-09-21"))

        self.assertEqual(1, len(posted))
        batch = posted[0]
        self.assertEqual(4, batch["version"])
        self.assertEqual({"fromDate": "2026-09-21", "toDate": "2026-09-21"}, batch["activityRange"])
        self.assertEqual([24444892080], [activity["activityId"] for activity in batch["activities"]])
        sleep_night_posted = dict(batch["sleepNights"][0])
        payload = sleep_night_posted.pop("garminResponse")
        self.assertEqual({
            "calendarDate": "2026-09-21",
            "startTimeUtc": "2026-09-20T20:54:00Z",
            "endTimeUtc": "2026-09-21T04:08:00Z",
            "sleepMinutes": 434,
            "deepSleepMinutes": 46,
            "lightSleepMinutes": 227,
            "remSleepMinutes": 161,
            "awakeSleepMinutes": 0,
            "unmeasurableSleepMinutes": 0,
        }, sleep_night_posted)
        # The payload is archived verbatim with every array-valued member removed (ADR 0010).
        self.assertTrue(no_lists(payload))
        raw = json.loads(sleep_night("2026-09-21").content)
        self.assertEqual(raw["dailySleepDTO"], payload["dailySleepDTO"])
        self.assertEqual({key for key, value in raw.items() if not isinstance(value, list)}, set(payload))
        summary_posted = dict(batch["dailySummaries"][0])
        self.assertEqual(json.loads(daily_summary("2026-09-21").content), summary_posted.pop("garminResponse"))
        self.assertEqual({
            "calendarDate": "2026-09-21",
            "bodyBattery": {"charged": 55, "drained": 54, "highest": 99, "lowest": 37},
            "stressLevel": {"average": 22, "maximum": 92, "restSeconds": 44100, "lowSeconds": 16500,
                            "mediumSeconds": 1980, "highSeconds": 660},
        }, summary_posted)

    def test_a_day_garmin_holds_nothing_for_is_posted_empty_and_never_retried(self):
        today, empty_day = date(2010, 1, 2), date(2010, 1, 1)

        _, posted = self.sync(today, empty_day, 30,
                              Response(200), Response(200, PROFILE), Response(204, b""), Response(204, b""),
                              Response(204, b""),
                              Response(200), sleep_night("2010-01-01"), daily_summary("2010-01-01"),
                              training_status("2010-01-01"))

        self.assertEqual(["2010-01-02", "2010-01-01"], [batch["activityRange"]["fromDate"] for batch in posted])
        for batch in posted:
            self.assertEqual(4, batch["version"])
            self.assertEqual(([], [], []), (batch["activities"], batch["sleepNights"], batch["dailySummaries"]))

        summary, reposted = self.sync(today, empty_day, 30,
                                      Response(200), Response(200, PROFILE), Response(204, b""), Response(204, b""),
                                      Response(204, b""))

        self.assertEqual((1, 0, 0), summary)
        self.assertEqual(["2010-01-02"], [batch["activityRange"]["fromDate"] for batch in reposted])

    def test_an_interruption_inside_a_day_posts_nothing_for_it_and_the_next_run_fetches_it_again(self):
        with self.assertRaises(GarminAbort):
            self.sync(date(2026, 9, 21), date(2026, 9, 20), 30,
                      *activities("2026-09-21"), Response(200, PROFILE),
                      sleep_night("2026-09-21"), daily_summary("2026-09-21"), training_status("2026-09-21"),
                      *activities("2026-09-20"), sleep_night("2026-09-20"), daily_summary("2026-09-20"),
                      Response(429))

        self.assertEqual(["2026-09-21"], [batch["activityRange"]["fromDate"] for batch in self.posted])

        _, posted = self.sync(date(2026, 9, 21), date(2026, 9, 20), 30,
                              *activities("2026-09-21"), Response(200, PROFILE),
                              sleep_night("2026-09-21"), daily_summary("2026-09-21"), training_status("2026-09-21"),
                              *activities("2026-09-20"), sleep_night("2026-09-20"), daily_summary("2026-09-20"),
                              training_status("2026-09-20"))

        self.assertEqual(["2026-09-21", "2026-09-20"], [batch["activityRange"]["fromDate"] for batch in posted])
        self.assertEqual(["2026-09-20"], [night["calendarDate"] for night in posted[1]["sleepNights"]])

    def test_a_privacy_protected_summary_stops_the_run_rather_than_completing_the_day_empty(self):
        protected = json.dumps({"calendarDate": "2026-09-20", "privacyProtected": True}).encode("utf-8")

        with self.assertRaisesRegex(RuntimeError, "privacy"):
            self.sync(date(2026, 9, 21), date(2026, 9, 20), 30,
                      Response(200), Response(200, PROFILE), Response(204, b""), Response(204, b""),
                      Response(204, b""), Response(200), Response(204, b""), Response(200, protected))

        self.assertEqual([date(2026, 9, 21), date(2026, 9, 20)],
                         Watermark(self.path).pending_days(date(2026, 9, 20), date(2026, 9, 21)))

    def test_a_response_dated_for_another_day_stops_the_run(self):
        with self.assertRaisesRegex(RuntimeError, "2026-09-20"):
            self.sync(date(2026, 9, 20), date(2026, 9, 20), 30,
                      Response(200), Response(200, PROFILE), sleep_night("2026-09-21"))


if __name__ == "__main__":
    unittest.main()
