"""Tests for the one piece of spike code whose output is committed.

`dump_garmin.py` writes real personal data to a gitignored directory, so a bug
there costs a re-fetch. `scrub_dumps.py` writes to a tracked directory, so a bug
there publishes an account identifier, a device identifier or a set of GPS
coordinates that locate a training session, and git keeps it. That asymmetry is
why this file exists while the rest of the spike is verified by reading dumps.

The input is synthetic throughout: a test that needs one athlete's own dumps to
run is a test nobody else can run.
"""

import json

import pytest

from scrub_dumps import (
    PLACEHOLDER_FULL_NAME,
    SCRUBBED_FIELDS,
    LeakError,
    scrub_run,
)

REAL_OWNER_ID = 103595846
REAL_DEVICE_ID = 3381107175
REAL_FULL_NAME = "Jacob Egholm Ottesen"
REAL_ACTIVITY_UUID = "f657112e-313b-4645-b592-2d3516818716"
REAL_DISPLAY_NAME = "bd931651-d18b-406b-a619-0917fbe3957a"


def list_entry(activity_id, **overrides):
    entry = {
        "activityId": activity_id,
        "activityName": "Hamstring Rehab",
        "activityUUID": REAL_ACTIVITY_UUID,
        "activityType": {"typeKey": "strength_training", "typeId": 13},
        "deviceId": REAL_DEVICE_ID,
        "duration": 3600.0,
        "endLatitude": 56.9083855021745,
        "endLongitude": 9.872989766299725,
        "locationName": "Aalborg",
        "manufacturer": "GARMIN",
        "ownerDisplayName": REAL_DISPLAY_NAME,
        "ownerFullName": REAL_FULL_NAME,
        "ownerId": REAL_OWNER_ID,
        "ownerProfileImageUrlLarge": (
            "https://s3.amazonaws.com/garmin-connect-prod/profile_images/"
            "dc47b4d9-fecd-48e0-9ccc-9335408b3f2f-103595846.png"
        ),
        "ownerProfileImageUrlMedium": None,
        "ownerProfileImageUrlSmall": None,
        "startLatitude": 56.909027975052595,
        "startLongitude": 9.873131588101387,
        "startTimeGMT": "2026-09-21 14:34:01",
        "startTimeLocal": "2026-09-21 16:34:01",
        "totalReps": 82,
        "totalSets": 14,
        "userRoles": ["SCOPE_ATP_READ", "ROLE_CONNECTUSER", "ROLE_WELLNESS_USER"],
        "workoutId": 1675565879,
    }
    entry.update(overrides)
    return entry


EXERCISE_SETS = {
    "activityId": 24444892080,
    "exerciseSets": [
        {
            "exercises": [
                {"category": "BENCH_PRESS", "name": None, "probability": 99.609375}
            ],
            "duration": 41.045,
            "repetitionCount": 8,
            "weight": 75000.0,
            "setType": "ACTIVE",
            "startTime": "2026-09-21T14:34:01.0",
            "wktStepIndex": 4,
            "messageIndex": 5,
        }
    ],
}

HR_TIME_IN_ZONES = [
    {"zoneNumber": 1, "secsInZone": 591.39, "zoneLowBoundary": 116},
]

HEART_RATE_ZONES = [
    {
        "trainingMethod": "HR_RESERVE",
        "restingHeartRateUsed": 51,
        "maxHeartRateUsed": 180,
        "zone1Floor": 116,
        "sport": "DEFAULT",
    }
]

INDEX = [
    {
        "file": "activity-service--exerciseSets__activity-24444892080.json",
        "endpoint": "/activity-service/activity/24444892080/exerciseSets",
        "params": None,
        "activityId": "24444892080",
        "bytes": 123,
        "fetchedAtUtc": "2026-09-22T09:05:30+00:00",
    }
]

PASSED_THROUGH = [
    "activity-service--exerciseSets__activity-24444892080.json",
    "activity-service--hrTimeInZones__activity-23965663197.json",
    "biometric-service--heartRateZones.json",
    "index.json",
    "exercise-set-cases.md",
]


def write_run(root, entries=None):
    run = root / "20260922T090450Z"
    run.mkdir(parents=True)
    if entries is None:
        entries = [list_entry(24444892080)]
    (run / "activitylist-service--search-activities__page-00.json").write_text(
        json.dumps(entries), encoding="utf-8"
    )
    (run / PASSED_THROUGH[0]).write_text(json.dumps(EXERCISE_SETS), encoding="utf-8")
    (run / PASSED_THROUGH[1]).write_text(json.dumps(HR_TIME_IN_ZONES), encoding="utf-8")
    (run / PASSED_THROUGH[2]).write_text(json.dumps(HEART_RATE_ZONES), encoding="utf-8")
    (run / PASSED_THROUGH[3]).write_text(json.dumps(INDEX), encoding="utf-8")
    (run / PASSED_THROUGH[4]).write_text("# cases\n", encoding="utf-8")
    return run


def scrubbed_entries(out):
    return json.loads(
        (out / "activitylist-service--search-activities__page-00.json").read_text(
            encoding="utf-8"
        )
    )


def test_every_scrubbed_field_keeps_its_key_so_the_payload_shape_survives(tmp_path):
    run = write_run(tmp_path / "dumps")
    out = tmp_path / "corpus"

    scrub_run(run, out)

    entry = scrubbed_entries(out)[0]
    for field in SCRUBBED_FIELDS:
        assert field in entry, f"{field} was dropped rather than replaced"


def test_no_scrubbed_value_survives_anywhere_in_the_written_bytes(tmp_path):
    run = write_run(tmp_path / "dumps")
    out = tmp_path / "corpus"

    scrub_run(run, out)

    written = "\n".join(
        path.read_text(encoding="utf-8") for path in sorted(out.iterdir())
    )
    for literal in (
        REAL_FULL_NAME,
        str(REAL_OWNER_ID),
        str(REAL_DEVICE_ID),
        REAL_ACTIVITY_UUID,
        REAL_DISPLAY_NAME,
        "s3.amazonaws.com",
        "Aalborg",
        "Hamstring Rehab",
        "SCOPE_ATP_READ",
        "56.909027975052595",
        "9.873131588101387",
        "56.9083855021745",
        "9.872989766299725",
    ):
        assert literal not in written, f"{literal!r} survived the scrub"


def test_fields_that_are_not_identifying_are_left_as_garmin_sent_them(tmp_path):
    run = write_run(tmp_path / "dumps")
    out = tmp_path / "corpus"

    scrub_run(run, out)

    entry = scrubbed_entries(out)[0]
    assert entry["activityId"] == 24444892080
    assert entry["workoutId"] == 1675565879
    assert entry["totalSets"] == 14
    assert entry["totalReps"] == 82
    assert entry["duration"] == 3600.0
    assert entry["startTimeGMT"] == "2026-09-21 14:34:01"
    assert entry["startTimeLocal"] == "2026-09-21 16:34:01"
    assert entry["activityType"] == {"typeKey": "strength_training", "typeId": 13}
    assert entry["manufacturer"] == "GARMIN"


def test_one_account_scrubs_to_one_placeholder_so_the_corpus_stays_joinable(tmp_path):
    run = write_run(
        tmp_path / "dumps",
        entries=[list_entry(24444892080), list_entry(24272601315)],
    )
    out = tmp_path / "corpus"

    scrub_run(run, out)

    first, second = scrubbed_entries(out)
    assert first["ownerId"] == second["ownerId"]
    assert first["deviceId"] == second["deviceId"]
    assert first["ownerFullName"] == PLACEHOLDER_FULL_NAME


def test_two_activities_keep_two_distinct_uuids(tmp_path):
    run = write_run(
        tmp_path / "dumps",
        entries=[
            list_entry(24444892080),
            list_entry(
                24272601315, activityUUID="aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"
            ),
        ],
    )
    out = tmp_path / "corpus"

    scrub_run(run, out)

    first, second = scrubbed_entries(out)
    assert first["activityUUID"] != second["activityUUID"]


def test_a_device_id_of_zero_stays_zero_because_it_means_no_device(tmp_path):
    run = write_run(tmp_path / "dumps", entries=[list_entry(24444892080, deviceId=0)])
    out = tmp_path / "corpus"

    scrub_run(run, out)

    assert scrubbed_entries(out)[0]["deviceId"] == 0


def test_a_null_coordinate_stays_null_rather_than_gaining_a_placeholder(tmp_path):
    run = write_run(
        tmp_path / "dumps",
        entries=[list_entry(24444892080, startLatitude=None, locationName=None)],
    )
    out = tmp_path / "corpus"

    scrub_run(run, out)

    entry = scrubbed_entries(out)[0]
    assert entry["startLatitude"] is None
    assert entry["locationName"] is None


def test_a_populated_profile_url_is_replaced_and_an_absent_one_stays_absent(tmp_path):
    run = write_run(tmp_path / "dumps")
    out = tmp_path / "corpus"

    scrub_run(run, out)

    entry = scrubbed_entries(out)[0]
    assert entry["ownerProfileImageUrlLarge"].startswith("https://example.invalid/")
    assert entry["ownerProfileImageUrlMedium"] is None
    assert entry["ownerProfileImageUrlSmall"] is None


@pytest.mark.parametrize("name", PASSED_THROUGH)
def test_files_with_no_identifying_field_are_copied_byte_for_byte(tmp_path, name):
    run = write_run(tmp_path / "dumps")
    out = tmp_path / "corpus"

    scrub_run(run, out)

    assert (out / name).read_bytes() == (run / name).read_bytes()


def test_scrubbing_the_output_again_changes_nothing(tmp_path):
    run = write_run(tmp_path / "dumps")
    once = tmp_path / "once"
    twice = tmp_path / "twice"

    scrub_run(run, once)
    scrub_run(once, twice)

    for path in sorted(once.iterdir()):
        assert path.read_bytes() == (twice / path.name).read_bytes()


def test_a_file_kind_the_rules_do_not_cover_aborts_rather_than_being_copied(tmp_path):
    """An `ABORT-*.txt` carries the request URL and Garmin's error body verbatim.

    Copying anything the rules were not written for is the way a leak gets in
    without a rule ever being wrong, so an unrecognised file is refused.
    """
    run = write_run(tmp_path / "dumps")
    (run / "ABORT-403-20260922T090450Z.txt").write_text(
        "status: 403\nurl: https://connectapi.garmin.com/...\nbody: ...\n",
        encoding="utf-8",
    )
    out = tmp_path / "corpus"

    with pytest.raises(LeakError, match="ABORT-403"):
        scrub_run(run, out)

    assert not out.exists() or not list(out.iterdir())


def test_a_run_with_no_activity_list_aborts_rather_than_publishing_unchecked(tmp_path):
    """No list page means no census, and a census of nothing passes everything."""
    run = write_run(tmp_path / "dumps")
    (run / "activitylist-service--search-activities__page-00.json").unlink()
    out = tmp_path / "corpus"

    with pytest.raises(LeakError, match="no Activity list"):
        scrub_run(run, out)

    assert not out.exists() or not list(out.iterdir())


def test_an_identifying_key_in_a_copied_payload_aborts(tmp_path):
    """The copied payloads are copied because they carry no identifying field.

    That is a fact about a payload shape Garmin can change, so it is checked on
    every run rather than trusted from the one census that established it.
    """
    run = write_run(tmp_path / "dumps")
    grown = dict(EXERCISE_SETS)
    # Deliberately not the id on the list entries: a value-based sweep would
    # catch that one by coincidence and prove nothing about the key check.
    grown["ownerId"] = 987654321
    (run / PASSED_THROUGH[0]).write_text(json.dumps(grown), encoding="utf-8")
    out = tmp_path / "corpus"

    with pytest.raises(LeakError, match="ownerId"):
        scrub_run(run, out)

    assert not out.exists() or not list(out.iterdir())


def test_a_one_character_activity_name_does_not_poison_the_sweep(tmp_path):
    """A raw value too short to be distinctive is checked by value, not by search.

    `A` occurs inside `ACTIVE` and `GARMIN`, so searching for it would refuse a
    corpus that is in fact clean.
    """
    run = write_run(
        tmp_path / "dumps", entries=[list_entry(24444892080, activityName="A")]
    )
    out = tmp_path / "corpus"

    scrub_run(run, out)

    assert scrubbed_entries(out)[0]["activityName"] == "strength_training"


def test_an_empty_activity_uuid_is_carried_through_rather_than_crashing(tmp_path):
    run = write_run(
        tmp_path / "dumps", entries=[list_entry(24444892080, activityUUID="")]
    )
    out = tmp_path / "corpus"

    scrub_run(run, out)

    assert scrubbed_entries(out)[0]["activityUUID"] == ""


def test_an_unhandled_identifying_field_aborts_instead_of_writing_a_leak(tmp_path):
    run = write_run(tmp_path / "dumps")
    out = tmp_path / "corpus"

    rules = {field: rule for field, rule in SCRUBBED_FIELDS.items()}
    del rules["ownerFullName"]

    with pytest.raises(LeakError, match="ownerFullName"):
        scrub_run(run, out, rules=rules)

    assert not out.exists() or not list(out.iterdir())
