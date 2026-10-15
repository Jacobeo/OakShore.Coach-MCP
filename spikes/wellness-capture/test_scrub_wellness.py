import json
import tempfile
import unittest
from pathlib import Path

from scrub_wellness import PLACEHOLDER_DISPLAY_NAME, LeakError, scrub_run


PROFILE = {"displayName": "athlete.handle", "fullName": "Real Person", "profileId": 987654321,
           "garminGUID": "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee", "showAge": True}
SLEEP = {"dailySleepDTO": {"id": 1789937640000, "userProfilePK": 987654321, "sleepTimeSeconds": 26040},
         "wellnessSpO2SleepSummaryDTO": {"userProfilePk": 987654321, "deviceId": 2222222222},
         "wellnessEpochSPO2DataDTOList": [{"userProfilePK": 987654321, "deviceId": 0, "spo2Reading": 93}],
         "remSleepData": None}
SUMMARY = {"userProfileId": 987654321, "userDailySummaryId": 987654321,
           "uuid": "11111111-2222-3333-4444-555555555555", "displayName": None, "totalSteps": 3191}


class ScrubWellnessTests(unittest.TestCase):
    def setUp(self):
        directory = tempfile.TemporaryDirectory()
        self.addCleanup(directory.cleanup)
        self.run_dir = Path(directory.name, "run")
        self.out_dir = Path(directory.name, "out")
        self.run_dir.mkdir()
        self.write("userprofile-service--socialProfile.json", PROFILE)
        self.write("wellness-service--dailySleepData__date-2026-09-21.json", SLEEP)
        self.write("usersummary-service--daily__date-2026-09-21.json", SUMMARY)
        (self.run_dir / "hrv-service--hrv__date-2026-09-21.json").write_bytes(b"")
        self.write("index.json", [
            {"file": "userprofile-service--socialProfile.json", "endpoint": "/userprofile-service/socialProfile"},
            {"file": "wellness-service--dailySleepData__date-2026-09-21.json",
             "endpoint": "/wellness-service/wellness/dailySleepData/athlete.handle"},
            {"file": "hrv-service--hrv__date-2026-09-21.json", "endpoint": "/hrv-service/hrv/2026-09-21", "status": 204},
        ])

    def write(self, name, payload):
        (self.run_dir / name).write_text(json.dumps(payload), encoding="utf-8")

    def read(self, name):
        return json.loads((self.out_dir / name).read_text(encoding="utf-8"))

    def test_identifiers_are_replaced_at_any_depth_and_figures_kept(self):
        published = scrub_run(self.run_dir, self.out_dir)

        self.assertEqual(["index-wellness.json",
                          "usersummary-service--daily__date-2026-09-21.json",
                          "wellness-service--dailySleepData__date-2026-09-21.json"], published)
        sleep = self.read("wellness-service--dailySleepData__date-2026-09-21.json")
        self.assertEqual({"id": 1789937640000, "userProfilePK": 1, "sleepTimeSeconds": 26040}, sleep["dailySleepDTO"])
        self.assertEqual({"userProfilePk": 1, "deviceId": 1}, sleep["wellnessSpO2SleepSummaryDTO"])
        self.assertEqual([{"userProfilePK": 1, "deviceId": 0, "spo2Reading": 93}], sleep["wellnessEpochSPO2DataDTOList"])
        self.assertEqual({"userProfileId": 1, "userDailySummaryId": 1,
                          "uuid": "00000000-0000-4000-9000-000000000001", "displayName": None, "totalSteps": 3191},
                         self.read("usersummary-service--daily__date-2026-09-21.json"))
        index = self.read("index-wellness.json")
        self.assertEqual([f"/wellness-service/wellness/dailySleepData/{PLACEHOLDER_DISPLAY_NAME}",
                          "/hrv-service/hrv/2026-09-21"], [entry["endpoint"] for entry in index])

    def test_a_file_no_rule_covers_publishes_nothing(self):
        (self.run_dir / "ABORT-401.txt").write_text("GET /x", encoding="utf-8")

        with self.assertRaisesRegex(LeakError, "No rule covers"):
            scrub_run(self.run_dir, self.out_dir)
        self.assertFalse(self.out_dir.exists())

    def test_a_profile_value_under_a_key_no_rule_names_publishes_nothing(self):
        self.write("usersummary-service--daily__date-2026-09-21.json", {**SUMMARY, "ownerName": "Real Person"})

        with self.assertRaisesRegex(LeakError, "survive"):
            scrub_run(self.run_dir, self.out_dir)
        self.assertFalse(self.out_dir.exists())


def training_status(*device_ids):
    return {"userId": 987654321,
            "mostRecentVO2Max": {"userId": 987654321, "generic": {"calendarDate": "2026-09-02", "vo2MaxPreciseValue": 39.3}},
            "mostRecentTrainingStatus": {
                "userId": 987654321,
                "latestTrainingStatusData": {str(device_id): {"calendarDate": "2026-09-21", "deviceId": device_id,
                                                              "weeklyTrainingLoad": 140}
                                             for device_id in device_ids},
                "recordedDevices": [{"deviceId": device_id, "deviceName": "Forerunner 245"} for device_id in device_ids]}}


class ScrubTrainingStatusTests(unittest.TestCase):
    """A training status run fetches no social profile, so what the rules replaced is the census."""

    def setUp(self):
        directory = tempfile.TemporaryDirectory()
        self.addCleanup(directory.cleanup)
        self.run_dir = Path(directory.name, "run")
        self.out_dir = Path(directory.name, "out")
        self.run_dir.mkdir()
        self.write("index.json", [{"file": "metrics-service--trainingstatus-aggregated__date-2026-09-21.json",
                                   "endpoint": "/metrics-service/metrics/trainingstatus/aggregated/2026-09-21"}])

    def write(self, name, payload):
        (self.run_dir / name).write_text(json.dumps(payload), encoding="utf-8")

    def test_the_account_id_and_the_device_id_are_replaced_even_where_the_device_id_is_a_key(self):
        self.write("metrics-service--trainingstatus-aggregated__date-2026-09-21.json", training_status(2222222222))

        published = scrub_run(self.run_dir, self.out_dir)

        self.assertEqual(["index-training-status.json",
                          "metrics-service--trainingstatus-aggregated__date-2026-09-21.json"], published)
        scrubbed = json.loads((self.out_dir / published[1]).read_text(encoding="utf-8"))
        self.assertEqual({"userId": 1,
                          "mostRecentVO2Max": {"userId": 1, "generic": {"calendarDate": "2026-09-02",
                                                                        "vo2MaxPreciseValue": 39.3}},
                          "mostRecentTrainingStatus": {
                              "userId": 1,
                              "latestTrainingStatusData": {"1": {"calendarDate": "2026-09-21", "deviceId": 1,
                                                                 "weeklyTrainingLoad": 140}},
                              "recordedDevices": [{"deviceId": 1, "deviceName": "Forerunner 245"}]}}, scrubbed)

    def test_two_devices_that_would_share_a_placeholder_publish_nothing(self):
        self.write("metrics-service--trainingstatus-aggregated__date-2026-09-21.json",
                   training_status(2222222222, 2222222223))

        with self.assertRaisesRegex(LeakError, "device"):
            scrub_run(self.run_dir, self.out_dir)
        self.assertFalse(self.out_dir.exists())

    def test_a_device_id_under_a_key_no_rule_names_publishes_nothing(self):
        payload = training_status(2222222222)
        payload["mostRecentTrainingStatus"]["primaryDevice"] = 2222222222
        self.write("metrics-service--trainingstatus-aggregated__date-2026-09-21.json", payload)

        with self.assertRaisesRegex(LeakError, "survive"):
            scrub_run(self.run_dir, self.out_dir)
        self.assertFalse(self.out_dir.exists())


if __name__ == "__main__":
    unittest.main()
