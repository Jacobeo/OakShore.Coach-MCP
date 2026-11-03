import sys
import tempfile
import types
import unittest
from unittest.mock import patch
from datetime import date, datetime, timedelta, timezone
from pathlib import Path


ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "src" / "sync"))

from garmin_client import GarminActivityClient, load_token_client  # noqa: E402
from sync_sign_in import SyncSignInRequired  # noqa: E402
from notify import NtfyNotifier, notifier_from_environment, run_notifying_failures  # noqa: E402
from sync_activities import run  # noqa: E402
from watermark import Watermark  # noqa: E402


DAY = date(2026, 9, 21)
# A fixed local time, so the "when" a message names is asserted rather than approximated.
WHEN = datetime(2026, 10, 20, 19, 4, 31, tzinfo=timezone(timedelta(hours=2)))


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


PROFILE = b'{"displayName": "00000000-0000-4000-8000-00000000000a"}'


def empty_day():
    """A day Garmin holds nothing for: the Activity list, the social profile behind the
    per-date reads, then sleep, the daily summary and training status, all empty."""
    return [Response(200, b"[]"), Response(200, PROFILE),
            Response(204), Response(204), Response(204)]


def sync_one_day(session, posted):
    """One dated day over the fake Garmin boundary, as the run would sync it."""
    def sync():
        with tempfile.TemporaryDirectory() as directory:
            run(DAY, DAY, DAY, 1, Watermark(Path(directory, "watermark.json")),
                GarminActivityClient(session, "https://garmin.example",
                                     lambda: {"Authorization": "Bearer fixture"},
                                     sleep=lambda _: None),
                posted.append)
    return sync


class ReauthenticationTests(unittest.TestCase):
    def test_a_rejected_token_asks_for_the_annual_login_rather_than_reporting_a_fault(self):
        posted, alerts = [], []

        exit_code = run_notifying_failures(sync_one_day(Session(Response(401)), posted),
                                           alerts.append, now=lambda: WHEN)

        self.assertEqual(1, exit_code)
        self.assertEqual([], posted)
        (alert,) = alerts
        self.assertEqual("Garmin sync needs its annual login", alert.title)
        self.assertIn("2026-10-20 19:04", alert.body)

    def test_a_rate_limit_reports_a_fault_and_never_the_annual_login(self):
        posted, alerts = [], []

        exit_code = run_notifying_failures(sync_one_day(Session(Response(429)), posted),
                                           alerts.append, now=lambda: WHEN)

        self.assertEqual(1, exit_code)
        self.assertEqual([], posted)
        (alert,) = alerts
        self.assertEqual("Garmin sync failed", alert.title)
        self.assertIn("429", alert.body)
        self.assertIn("2026-10-20 19:04", alert.body)
        self.assertNotIn("login", alert.body)

    def test_a_spent_coach_sign_in_is_its_own_message_and_never_the_garmin_one(self):
        # Two sign-ins fall due independently; a message naming the wrong one sends
        # the athlete to the wrong machine.
        alerts = []

        def sync():
            raise SyncSignInRequired("WorkOS no longer accepts the saved sign in")

        exit_code = run_notifying_failures(sync, alerts.append, now=lambda: WHEN)

        self.assertEqual(1, exit_code)
        (alert,) = alerts
        self.assertEqual("Coach sign-in needs renewing", alert.title)
        self.assertNotIn("Garmin", alert.title)
        self.assertNotIn("Garmin", alert.body)
        self.assertEqual("urgent", alert.priority)
        self.assertIn("2026-10-20 19:04", alert.body)


class TransientFailureTests(unittest.TestCase):
    def test_a_failure_that_succeeds_on_retry_notifies_nothing(self):
        for failure in (OSError("connection lost"), Response(503)):
            with self.subTest(failure=failure):
                posted, alerts = [], []
                session = Session(failure, *empty_day())

                exit_code = run_notifying_failures(sync_one_day(session, posted),
                                                   alerts.append, now=lambda: WHEN)

                self.assertEqual(0, exit_code)
                self.assertEqual([], alerts)
                self.assertEqual(1, len(posted))

    def test_a_failure_that_outlives_its_retries_reports_the_fault(self):
        for failure in (OSError("connection lost"), Response(503)):
            with self.subTest(failure=failure):
                posted, alerts = [], []

                exit_code = run_notifying_failures(
                    sync_one_day(Session(failure, failure, failure), posted),
                    alerts.append, now=lambda: WHEN)

                self.assertEqual(1, exit_code)
                self.assertEqual([], posted)
                (alert,) = alerts
                self.assertEqual("Garmin sync failed", alert.title)
                self.assertIn("2026-10-20 19:04", alert.body)


class NtfyTests(unittest.TestCase):
    def notifier(self, sent, token=None):
        return NtfyNotifier("https://ntfy.example", "coach-s3cret", token,
                            send=lambda url, headers, body: sent.append((url, headers, body)))

    def test_an_alert_is_posted_to_the_configured_topic_as_a_titled_message(self):
        posted, sent = [], []

        run_notifying_failures(sync_one_day(Session(Response(429)), posted),
                               self.notifier(sent), now=lambda: WHEN)

        (url, headers, body) = sent[0]
        self.assertEqual("https://ntfy.example/coach-s3cret", url)
        self.assertEqual("Garmin sync failed", headers["Title"])
        self.assertIn(b"429", body)
        self.assertNotIn("Authorization", headers)

    def test_the_annual_login_is_the_more_urgent_of_the_two_messages(self):
        posted, sent = [], []

        run_notifying_failures(sync_one_day(Session(Response(401)), posted),
                               self.notifier(sent), now=lambda: WHEN)
        run_notifying_failures(sync_one_day(Session(Response(429)), posted),
                               self.notifier(sent), now=lambda: WHEN)

        (_, reauth, _), (_, failure, _) = sent
        self.assertEqual("urgent", reauth["Priority"])
        self.assertEqual("high", failure["Priority"])

    def test_a_protected_topic_carries_its_token(self):
        posted, sent = [], []

        run_notifying_failures(sync_one_day(Session(Response(429)), posted),
                               self.notifier(sent, token="tk_abc"), now=lambda: WHEN)

        self.assertEqual("Bearer tk_abc", sent[0][1]["Authorization"])

    def test_a_notification_that_cannot_be_sent_reports_the_fault_without_the_topic(self):
        posted, complaints = [], []

        def refuse(url, headers, body):
            raise RuntimeError(f"POST {url} was refused")

        notifier = NtfyNotifier("https://ntfy.example", "coach-s3cret", send=refuse)
        exit_code = run_notifying_failures(sync_one_day(Session(Response(429)), posted),
                                           notifier, now=lambda: WHEN, warn=complaints.append)

        said = " ".join(complaints)
        self.assertEqual(1, exit_code)
        self.assertNotIn("coach-s3cret", said)
        self.assertIn("<topic>", said)
        self.assertIn("429", said)

    def test_a_plaintext_server_is_refused_because_the_topic_travels_in_the_url(self):
        with self.assertRaisesRegex(ValueError, "HTTPS"):
            NtfyNotifier("http://ntfy.example", "coach-s3cret")


class ConfigurationTests(unittest.TestCase):
    def test_the_topic_server_and_token_are_read_from_the_home_machine_environment(self):
        posted, sent = [], []
        notify = notifier_from_environment(
            {"COACH_NTFY_TOPIC": "coach-s3cret", "COACH_NTFY_SERVER": "https://ntfy.example",
             "COACH_NTFY_TOKEN": "tk_abc"},
            send=lambda url, headers, body: sent.append((url, headers, body)))

        run_notifying_failures(sync_one_day(Session(Response(429)), posted), notify,
                               now=lambda: WHEN)

        (url, headers, _) = sent[0]
        self.assertEqual("https://ntfy.example/coach-s3cret", url)
        self.assertEqual("Bearer tk_abc", headers["Authorization"])

    def test_ntfy_sh_is_the_server_when_none_is_configured(self):
        posted, sent = [], []
        notify = notifier_from_environment(
            {"COACH_NTFY_TOPIC": "coach-s3cret"},
            send=lambda url, headers, body: sent.append((url, headers, body)))

        run_notifying_failures(sync_one_day(Session(Response(429)), posted), notify,
                               now=lambda: WHEN)

        self.assertEqual("https://ntfy.sh/coach-s3cret", sent[0][0])

    def test_a_machine_with_no_topic_configured_warns_once_and_still_syncs(self):
        posted, complaints = [], []

        notify = notifier_from_environment({}, warn=complaints.append)
        exit_code = run_notifying_failures(sync_one_day(Session(*empty_day()), posted), notify,
                                           now=lambda: WHEN, warn=complaints.append)

        self.assertEqual(0, exit_code)
        self.assertEqual(1, len(posted))
        self.assertIn("COACH_NTFY_TOPIC", " ".join(complaints))

    def test_an_unusable_setting_warns_and_still_syncs_rather_than_stopping_the_run(self):
        posted, complaints = [], []

        notify = notifier_from_environment(
            {"COACH_NTFY_TOPIC": "coach-s3cret", "COACH_NTFY_SERVER": "http://nas.local"},
            warn=complaints.append)
        exit_code = run_notifying_failures(sync_one_day(Session(*empty_day()), posted), notify,
                                           now=lambda: WHEN, warn=complaints.append)

        said = " ".join(complaints)
        self.assertEqual(0, exit_code)
        self.assertEqual(1, len(posted))
        self.assertIn("HTTPS", said)
        self.assertNotIn("coach-s3cret", said)

    def test_a_machine_with_no_topic_configured_still_reports_a_failure_to_its_logs(self):
        posted, complaints = [], []

        notify = notifier_from_environment({}, warn=complaints.append)
        exit_code = run_notifying_failures(sync_one_day(Session(Response(401)), posted), notify,
                                           now=lambda: WHEN, warn=complaints.append)

        self.assertEqual(1, exit_code)
        self.assertIn("Garmin sync needs its annual login", " ".join(complaints))


def garminconnect_modules(inner):
    package = types.ModuleType("garminconnect")
    package.Garmin = lambda: types.SimpleNamespace(client=inner)
    submodule = types.ModuleType("garminconnect.client")
    submodule.token_file_path = lambda path: Path(path) / "garmin_tokens.json"
    return {"garminconnect": package, "garminconnect.client": submodule}


class SavedToken:
    """The saved token as garminconnect presents it, refusing the way under test."""

    def __init__(self, authenticated=True, refresh_error=None):
        self.is_authenticated = authenticated
        self._refresh_error = refresh_error

    def load(self, path):
        pass

    def _token_expires_soon(self):
        return self._refresh_error is not None

    def _refresh_di_token(self):
        raise self._refresh_error

    def dump(self, path):
        pass


class SavedTokenTests(unittest.TestCase):
    def load(self, directory, inner):
        with patch.dict(sys.modules, garminconnect_modules(inner)):
            return load_token_client(directory, sleep=lambda _: None)

    def test_a_token_that_is_gone_or_refused_asks_for_the_annual_login(self):
        cases = {
            "no token file saved": (False, SavedToken()),
            "a token that is not authenticated": (True, SavedToken(authenticated=False)),
            "a refresh Garmin rejects": (True, SavedToken(
                refresh_error=RuntimeError("DI token refresh failed: 401"))),
        }
        for name, (write_file, inner) in cases.items():
            with self.subTest(name), tempfile.TemporaryDirectory() as directory:
                if write_file:
                    Path(directory, "garmin_tokens.json").write_text("fixture", encoding="utf-8")
                alerts = []

                exit_code = run_notifying_failures(lambda: self.load(directory, inner),
                                                   alerts.append, now=lambda: WHEN)

                self.assertEqual(1, exit_code)
                self.assertEqual("Garmin sync needs its annual login", alerts[0].title)

    def test_a_refresh_Garmin_could_not_serve_is_a_fault_not_a_login(self):
        with tempfile.TemporaryDirectory() as directory:
            Path(directory, "garmin_tokens.json").write_text("fixture", encoding="utf-8")
            inner = SavedToken(refresh_error=RuntimeError("DI token refresh failed: 503"))
            alerts = []

            exit_code = run_notifying_failures(lambda: self.load(directory, inner),
                                               alerts.append, now=lambda: WHEN)

            self.assertEqual(1, exit_code)
            self.assertEqual("Garmin sync failed", alerts[0].title)


if __name__ == "__main__":
    unittest.main()
