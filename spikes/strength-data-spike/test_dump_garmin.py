"""The only automated tests in this spike.

Pacing, the abort rule, and "no Garmin write of any kind" are the acceptance
criteria that cannot be demonstrated by hand without provoking a real block or
a real write, so they are driven here against a fake session instead.
Everything else in the spike is verified by reading the dumps.
"""

from types import SimpleNamespace

import pytest

from dump_garmin import (
    ABORT_STATUSES,
    HEART_RATE_ZONES_PATH,
    AbortSignal,
    DumpWriter,
    PacedReader,
    cardio_activities,
    fetch_exercise_sets,
    fetch_heart_rate_zones,
    fetch_hr_time_in_zones,
    zone_attribution,
)


class FakeResponse:
    def __init__(self, status_code, content=b"", headers=None):
        self.status_code = status_code
        self.content = content
        self.headers = headers or {}


class FakeSession:
    def __init__(self, *responses):
        self.responses = list(responses)
        self.calls = []

    def request(self, method, url, headers=None, params=None, timeout=None):
        self.calls.append(SimpleNamespace(method=method, url=url, params=params))
        return self.responses.pop(0)


def make_reader(*responses, pace=4.0, already_called=False):
    session = FakeSession(*responses)
    slept = []
    reader = PacedReader(
        session=session,
        base_url="https://connectapi.example",
        headers=lambda: {"Authorization": "Bearer x"},
        pace_seconds=pace,
        sleep=slept.append,
        already_called=already_called,
    )
    return reader, session, slept


@pytest.mark.parametrize("status", ABORT_STATUSES)
def test_aborts_on_first_sight_of_an_auth_or_rate_limit_status(status):
    reader, session, _ = make_reader(
        FakeResponse(status, b'{"message":"nope"}', {"Retry-After": "600"}),
        FakeResponse(200, b"[]"),
    )

    with pytest.raises(AbortSignal) as caught:
        reader.get("/activitylist-service/activities/search/activities")

    assert caught.value.status_code == status
    assert caught.value.retry_after == "600"
    assert caught.value.retrying_would_extend_the_block
    assert len(session.calls) == 1, "aborting must not retry the failed call"


def test_a_second_call_is_never_made_after_an_abort():
    reader, session, _ = make_reader(FakeResponse(429, b""), FakeResponse(200, b"[]"))

    with pytest.raises(AbortSignal):
        reader.get("/first")
    with pytest.raises(AbortSignal) as caught:
        reader.get("/second")

    assert caught.value.status_code == 429, "the original abort is what stands"
    assert len(session.calls) == 1


def test_other_error_statuses_also_stop_the_run_without_retrying():
    reader, session, _ = make_reader(
        FakeResponse(500, b"boom"), FakeResponse(200, b"[]")
    )

    with pytest.raises(AbortSignal) as caught:
        reader.get("/activitylist-service/activities/search/activities")

    assert not caught.value.retrying_would_extend_the_block
    assert len(session.calls) == 1


def test_successive_calls_are_paced_and_the_first_is_not_delayed():
    reader, _, slept = make_reader(
        FakeResponse(200, b"[1]"), FakeResponse(200, b"[2]"), FakeResponse(200, b"[]")
    )

    reader.get("/a")
    assert slept == []

    reader.get("/b")
    reader.get("/c")
    assert slept == [4.0, 4.0]


def test_the_call_after_login_is_paced_behind_it():
    reader, _, slept = make_reader(FakeResponse(200, b"[]"), already_called=True)

    reader.get("/a")

    assert slept == [4.0]


def test_the_pace_is_at_least_a_few_seconds_by_default():
    assert PacedReader.DEFAULT_PACE_SECONDS >= 3.0


def test_returns_the_response_bytes_untouched():
    body = b'{"activityId": 1, "name": "Str\xc3\xb8mtraening"}   \n'
    reader, _, _ = make_reader(FakeResponse(200, body))

    assert reader.get("/a") == body


def test_only_ever_issues_get_requests():
    reader, session, _ = make_reader(FakeResponse(200, b"[]"))

    reader.get("/a", params={"start": "0"})

    assert [call.method for call in session.calls] == ["GET"]
    assert session.calls[0].url == "https://connectapi.example/a"
    assert session.calls[0].params == {"start": "0"}


def strength_activity(activity_id, workout_id=None):
    return {
        "activityId": activity_id,
        "workoutId": workout_id,
        "activityType": {"typeKey": "strength_training"},
        "totalSets": 3,
    }


def test_the_per_activity_pass_is_paced_between_activities(tmp_path):
    reader, _, slept = make_reader(
        FakeResponse(200, b'{"exerciseSets": []}'),
        FakeResponse(200, b'{"exerciseSets": []}'),
        FakeResponse(200, b'{"exerciseSets": []}'),
        already_called=True,
    )
    writer = DumpWriter(tmp_path / "run")

    fetch_exercise_sets(
        reader,
        writer,
        [strength_activity(1), strength_activity(2), strength_activity(3)],
    )

    assert slept == [4.0, 4.0, 4.0], "the first call is paced behind the list pass"


def test_an_abort_mid_pass_leaves_the_remaining_activities_unfetched(tmp_path):
    reader, session, _ = make_reader(
        FakeResponse(200, b'{"exerciseSets": []}'),
        FakeResponse(429, b"", {"Retry-After": "600"}),
        FakeResponse(200, b'{"exerciseSets": []}'),
    )
    writer = DumpWriter(tmp_path / "run")

    with pytest.raises(AbortSignal) as caught:
        fetch_exercise_sets(
            reader,
            writer,
            [strength_activity(1), strength_activity(2), strength_activity(3)],
        )

    assert caught.value.status_code == 429
    assert len(session.calls) == 2, "the third Activity is never requested"


def cardio_activity(activity_id, type_key="running"):
    return {"activityId": activity_id, "activityType": {"typeKey": type_key}}


def test_cardio_is_every_activity_the_exercise_sets_pass_did_not_take():
    activities = [
        cardio_activity(1, "running"),
        strength_activity(2),
        cardio_activity(3, "indoor_cycling"),
    ]

    picked = cardio_activities([(None, activities)])

    assert [act["activityId"] for act in picked] == [1, 3]


def test_the_cardio_pass_is_paced_between_activities(tmp_path):
    reader, _, slept = make_reader(
        FakeResponse(200, b"[]"),
        FakeResponse(200, b"[]"),
        already_called=True,
    )
    writer = DumpWriter(tmp_path / "run")

    fetch_hr_time_in_zones(
        reader, writer, [cardio_activity(1), cardio_activity(2, "indoor_cycling")]
    )

    assert slept == [4.0, 4.0], "the first call is paced behind the pass before it"


def test_each_cardio_dump_is_named_for_its_activity_and_its_endpoint(tmp_path):
    reader, _, _ = make_reader(FakeResponse(200, b"[]"))
    writer = DumpWriter(tmp_path / "run")

    fetch_hr_time_in_zones(reader, writer, [cardio_activity(7)])

    assert (
        tmp_path / "run" / "activity-service--hrTimeInZones__activity-7.json"
    ).exists()


def test_an_abort_mid_cardio_pass_leaves_the_remaining_activities_unfetched(tmp_path):
    reader, session, _ = make_reader(
        FakeResponse(200, b"[]"),
        FakeResponse(429, b"", {"Retry-After": "600"}),
        FakeResponse(200, b"[]"),
    )
    writer = DumpWriter(tmp_path / "run")

    with pytest.raises(AbortSignal) as caught:
        fetch_hr_time_in_zones(
            reader,
            writer,
            [cardio_activity(1), cardio_activity(2), cardio_activity(3)],
        )

    assert caught.value.status_code == 429
    assert len(session.calls) == 2, "the third Activity is never requested"


def test_the_cardio_pass_never_reaches_for_the_zone_boundaries(tmp_path):
    """Why this rather than a test that `main` fetches them once.

    `fetch_heart_rate_zones` takes no Activity, so it cannot be per-Activity
    by construction; the only way the boundaries could be fetched repeatedly
    is the per-Activity pass asking for them. That is what is asserted here.
    """
    reader, session, _ = make_reader(FakeResponse(200, b"[]"), FakeResponse(200, b"[]"))
    writer = DumpWriter(tmp_path / "run")

    fetch_hr_time_in_zones(reader, writer, [cardio_activity(1), cardio_activity(2)])

    assert len(session.calls) == 2, "one call per Activity and nothing besides"
    assert not [
        call for call in session.calls if call.url.endswith(HEART_RATE_ZONES_PATH)
    ]


def test_the_boundaries_dump_is_named_for_the_endpoint_and_no_activity(tmp_path):
    reader, _, _ = make_reader(FakeResponse(200, b"[]"))
    writer = DumpWriter(tmp_path / "run")

    fetch_heart_rate_zones(reader, writer)

    assert (tmp_path / "run" / "biometric-service--heartRateZones.json").exists()


def zone_profile(sport, *floors):
    return {"sport": sport} | {
        f"zone{number}Floor": floor for number, floor in enumerate(floors, start=1)
    }


def test_two_profiles_sharing_floors_are_reported_as_ambiguous():
    """The one branch this athlete's data can never exercise.

    They have a single profile, so a dump that matches two of them cannot be
    produced by hand - and naming whichever came last is exactly the silent
    cross-sport wrongness the boundaries call exists to prevent.
    """
    profiles = [zone_profile("DEFAULT", 116, 128), zone_profile("RUNNING", 116, 128)]

    attribution = zone_attribution(profiles, (116, 128))

    assert "DEFAULT" in attribution and "RUNNING" in attribution


def test_boundaries_no_configured_profile_states_are_called_out():
    profiles = [zone_profile("DEFAULT", 116, 128)]

    assert zone_attribution(profiles, (99, 110)) == "no configured profile"
    assert zone_attribution(profiles, ()) == "unreadable"
    assert zone_attribution(profiles, (116, None)) == "unreadable"
