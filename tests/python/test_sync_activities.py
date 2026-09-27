import sys
import tempfile
import types
import unittest
from datetime import date
from pathlib import Path
from unittest.mock import patch


ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "src" / "sync"))

from garmin_client import GarminAbort, GarminActivityClient, load_token_client  # noqa: E402
from sync_activities import run  # noqa: E402


FIXTURE = (ROOT / "fixtures" / "garmin" /
           "activitylist-service--search-activities__page-00.json").read_bytes()
START = date(2026, 8, 5)
END = date(2026, 9, 22)


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
    def test_replays_activity_fixture_and_posts_one_version_two_batch(self):
        session = Session(Response(200, FIXTURE))
        sleeps = []
        posted = []

        count = run(START, END, client(session, sleeps), posted.append)

        self.assertEqual(15, count)
        self.assertEqual([4.0], sleeps)
        self.assertEqual(1, len(session.calls))
        method, url, kwargs = session.calls[0]
        self.assertEqual("GET", method)
        self.assertEqual("https://garmin.example/activitylist-service/activities/search/activities", url)
        self.assertEqual({"startDate": "2026-08-05", "endDate": "2026-09-22",
                          "start": "0", "limit": "20"}, kwargs["params"])
        self.assertEqual(1, len(posted))
        batch = posted[0]
        self.assertEqual(2, batch["version"])
        self.assertEqual({"fromDate": "2026-08-05", "toDate": "2026-09-22"}, batch["activityRange"])
        self.assertEqual(15, len(batch["activities"]))
        first = batch["activities"][0]
        self.assertEqual(24444892080, first["activityId"])
        self.assertEqual("2026-09-21T14:34:01Z", first["startTimeUtc"])
        self.assertEqual("strength_training", first["typeKey"])
        self.assertEqual(14, first["totalSets"])
        self.assertEqual(82, first["totalReps"])

    def test_empty_range_still_posts_a_batch(self):
        posted = []
        run(START, END, client(Session(Response(200)), []), posted.append)
        self.assertEqual([], posted[0]["activities"])

    def test_auth_challenge_and_rate_limit_abort_without_retry_or_post(self):
        for status in (401, 403, 429):
            with self.subTest(status=status):
                session = Session(Response(status))
                posted = []
                with self.assertRaises(GarminAbort) as raised:
                    run(START, END, client(session, []), posted.append)
                self.assertEqual(status, raised.exception.status)
                self.assertEqual(1, len(session.calls))
                self.assertEqual([], posted)

    def test_network_and_server_failures_retry_with_backoff(self):
        for failure in (OSError("connection lost"), Response(503)):
            with self.subTest(failure=failure):
                session = Session(failure, Response(200, FIXTURE))
                sleeps = []
                posted = []
                run(START, END, client(session, sleeps), posted.append)
                self.assertEqual([4.0, 1, 4.0], sleeps)
                self.assertEqual(2, len(session.calls))
                self.assertEqual(15, len(posted[0]["activities"]))

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
