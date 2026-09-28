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


FIXTURE_ENTRIES = json.loads((ROOT / "fixtures" / "garmin" /
                              "activitylist-service--search-activities__page-00.json")
                             .read_text(encoding="utf-8"))
TODAY = date(2026, 9, 21)  # the fixture's most recent Activity day (id 24444892080)


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


def client(session):
    return GarminActivityClient(session, "https://garmin.example",
                                lambda: {"Authorization": "Bearer fixture"}, sleep=lambda _: None)


def day_response(day):
    entries = [entry for entry in FIXTURE_ENTRIES
               if entry["startTimeGMT"].startswith(day.isoformat())]
    return Response(200, json.dumps(entries).encode("utf-8"))


def requested_days(session):
    days = []
    for _, _, kwargs in session.calls:
        params = kwargs["params"]
        assert params["startDate"] == params["endDate"]
        days.append(params["startDate"])
    return days


class WatermarkRunTests(unittest.TestCase):
    def setUp(self):
        directory = tempfile.TemporaryDirectory()
        self.addCleanup(directory.cleanup)
        self.path = Path(directory.name, "watermark.json")
        self.watermark = Watermark(self.path)

    def sync(self, today, earliest, max_days, *results, watermark=None):
        session = Session(*results)
        posted = []
        summary = run_watermark(today, earliest, max_days,
                                watermark or Watermark(self.path), client(session), posted.append)
        return summary, requested_days(session), posted

    def test_first_run_syncs_from_the_earliest_date_most_recent_first(self):
        days = [date(2026, 9, 21), date(2026, 9, 20), date(2026, 9, 19),
                date(2026, 9, 18), date(2026, 9, 17)]

        summary, requested, posted = self.sync(TODAY, date(2026, 9, 17), 30,
                                               *[day_response(day) for day in days])

        self.assertEqual((5, 2, 0), summary)
        self.assertEqual([day.isoformat() for day in days], requested)
        for batch, day in zip(posted, days):
            self.assertEqual(2, batch["version"])
            self.assertEqual({"fromDate": day.isoformat(), "toDate": day.isoformat()},
                             batch["activityRange"])
        self.assertEqual([24444892080], [a["activityId"] for a in posted[0]["activities"]])
        self.assertEqual([], posted[1]["activities"])
        self.assertEqual([24399329227], [a["activityId"] for a in posted[4]["activities"]])

    def test_next_run_continues_from_the_last_completed_date_and_resyncs_today(self):
        earliest = date(2026, 9, 19)
        self.sync(date(2026, 9, 20), earliest, 30,
                  day_response(date(2026, 9, 20)), day_response(date(2026, 9, 19)))

        _, requested, _ = self.sync(TODAY, earliest, 30,
                                    day_response(TODAY), day_response(date(2026, 9, 20)))

        self.assertEqual(["2026-09-21", "2026-09-20"], requested)

    def test_running_twice_in_an_hour_resyncs_only_today(self):
        earliest = date(2026, 9, 19)
        self.sync(TODAY, earliest, 30, day_response(TODAY),
                  day_response(date(2026, 9, 20)), day_response(date(2026, 9, 19)))

        summary, requested, posted = self.sync(TODAY, earliest, 30, day_response(TODAY))

        self.assertEqual((1, 1, 0), summary)
        self.assertEqual(["2026-09-21"], requested)
        self.assertEqual([24444892080], [a["activityId"] for a in posted[0]["activities"]])

    def test_ceiling_bounds_each_run_and_a_long_gap_closes_over_several_runs(self):
        earliest = date(2026, 9, 1)
        today = date(2026, 9, 27)

        first, first_days, _ = self.sync(today, earliest, 10, *[Response(200)] * 10)
        second, second_days, _ = self.sync(today, earliest, 10, *[Response(200)] * 10)
        third, third_days, _ = self.sync(today, earliest, 10, *[Response(200)] * 9)

        self.assertEqual((10, 0, 17), first)
        self.assertEqual([f"2026-09-{day}" for day in range(27, 17, -1)], first_days)
        self.assertEqual((10, 0, 8), second)
        self.assertEqual(["2026-09-27"] + [f"2026-09-{day:02}" for day in range(17, 8, -1)],
                         second_days)
        self.assertEqual((9, 0, 0), third)
        self.assertEqual(["2026-09-27"] + [f"2026-09-{day:02}" for day in range(8, 0, -1)],
                         third_days)

    def test_interrupted_run_keeps_committed_days_and_the_next_run_continues(self):
        earliest = date(2026, 9, 17)
        session = Session(day_response(TODAY), day_response(date(2026, 9, 20)), Response(429))
        posted = []

        with self.assertRaises(GarminAbort):
            run_watermark(TODAY, earliest, 30, self.watermark, client(session), posted.append)

        self.assertEqual(["2026-09-21", "2026-09-20"],
                         [batch["activityRange"]["fromDate"] for batch in posted])

        _, requested, _ = self.sync(TODAY, earliest, 30, *[Response(200)] * 4)
        self.assertEqual(["2026-09-21", "2026-09-19", "2026-09-18", "2026-09-17"], requested)

    def test_rerunning_an_already_synced_period_reposts_the_same_delivery(self):
        earliest = date(2026, 9, 19)
        _, _, first_posted = self.sync(TODAY, earliest, 30, day_response(TODAY),
                                       day_response(date(2026, 9, 20)),
                                       day_response(date(2026, 9, 19)))

        _, requested, reposted = self.sync(TODAY, earliest, 30, day_response(TODAY))

        self.assertEqual(["2026-09-21"], requested)
        self.assertEqual([first_posted[0]], reposted)

    def test_unreadable_watermark_file_fails_rather_than_resyncing_everything(self):
        self.path.write_text("not json", encoding="utf-8")
        with self.assertRaisesRegex(RuntimeError, "unreadable"):
            Watermark(self.path)

    def test_rejects_a_ceiling_below_one_day(self):
        with self.assertRaises(ValueError):
            run_watermark(TODAY, date(2026, 9, 19), 0, self.watermark,
                          client(Session()), lambda batch: None)


if __name__ == "__main__":
    unittest.main()
