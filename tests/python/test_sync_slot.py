"""Two runs overlapping on the home machine. The scheduled catch-up and a manual run
right after an Activity can land together, and both write the same watermark and post
the same days, so only one may proceed."""

import sys
import tempfile
import unittest
from pathlib import Path


ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "src" / "sync"))

from sync_slot import AlreadyRunning, sync_slot  # noqa: E402
from sync_activities import run_unless_already_running  # noqa: E402


class SyncSlotTests(unittest.TestCase):
    def setUp(self):
        self.directory = tempfile.TemporaryDirectory()
        self.addCleanup(self.directory.cleanup)
        self.slot_file = Path(self.directory.name) / "sync.lock"

    def test_a_second_run_is_refused_while_the_first_holds_the_slot(self):
        with sync_slot(self.slot_file):
            with self.assertRaises(AlreadyRunning):
                with sync_slot(self.slot_file):
                    self.fail("a second run proceeded alongside the first")

    def test_the_slot_is_free_once_the_first_run_finishes(self):
        with sync_slot(self.slot_file):
            pass

        with sync_slot(self.slot_file):
            pass

    def test_a_run_that_failed_still_frees_the_slot(self):
        # A run that aborts on a rate limit must not lock the machine out until a reboot.
        with self.assertRaises(RuntimeError):
            with sync_slot(self.slot_file):
                raise RuntimeError("Garmin returned HTTP 429; sync stopped")

        with sync_slot(self.slot_file):
            pass


class OverlappingRunTests(unittest.TestCase):
    """What a manual run does when the evening catch-up is already going."""

    def setUp(self):
        self.directory = tempfile.TemporaryDirectory()
        self.addCleanup(self.directory.cleanup)
        self.slot_file = Path(self.directory.name) / "sync.lock"

    def run_second(self):
        reached, alerts, complaints = [], [], []
        with sync_slot(self.slot_file):
            exit_code = run_unless_already_running(
                self.slot_file, lambda: reached.append("synced"), alerts.append,
                warn=complaints.append)
        return exit_code, reached, alerts, complaints

    def test_the_second_run_fetches_nothing_rather_than_posting_a_day_twice(self):
        exit_code, reached, alerts, complaints = self.run_second()

        self.assertEqual([], reached)
        self.assertEqual(0, exit_code)
        self.assertIn("already", " ".join(complaints).lower())

    def test_the_second_run_is_not_a_failure_and_reaches_no_phone(self):
        # An overlap is the design working, not a fault; paging the athlete for it
        # would train them to ignore the one message that matters.
        _, _, alerts, _ = self.run_second()

        self.assertEqual([], alerts)

    def test_a_run_with_the_slot_free_proceeds_and_reports_its_own_failures(self):
        alerts, complaints = [], []

        def sync():
            raise RuntimeError("Garmin returned HTTP 429; sync stopped")

        exit_code = run_unless_already_running(self.slot_file, sync, alerts.append,
                                               warn=complaints.append)

        self.assertEqual(1, exit_code)
        self.assertEqual(1, len(alerts))


if __name__ == "__main__":
    unittest.main()
