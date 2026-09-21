"""The only automated tests in this spike.

Pacing, the abort rule, and "no Garmin write of any kind" are the acceptance
criteria that cannot be demonstrated by hand without provoking a real block or
a real write, so they are driven here against a fake session instead.
Everything else in the spike is verified by reading the dumps.
"""

from types import SimpleNamespace

import pytest

from dump_garmin import ABORT_STATUSES, AbortSignal, PacedReader


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
