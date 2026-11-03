"""The push notification a failed run sends to the athlete's phone, from the home
machine (ADR 0001). Azure cannot tell a failed sync from a machine switched off, so
it sends nothing. A fault that healed on retry never reaches here: only a run that
gave up does. The topic is the only credential the service has, so it lives in the
home machine's environment and is kept out of logs and this repository."""

from __future__ import annotations

import os
import sys
from dataclasses import dataclass
from datetime import datetime
from collections.abc import Mapping
from typing import Any, Callable
from urllib.request import Request, urlopen

from garmin_client import GarminReauthRequired
from sync_sign_in import SyncSignInRequired


REAUTH_TITLE = "Garmin sync needs its annual login"
COACH_REAUTH_TITLE = "Coach sign-in needs renewing"
FAILURE_TITLE = "Garmin sync failed"
DEFAULT_SERVER = "https://ntfy.sh"


@dataclass(frozen=True)
class SyncAlert:
    """One message about a run that gave up, named for what the athlete must do."""
    title: str
    body: str
    priority: str
    tags: str


def local_time(when: datetime) -> str:
    return when.strftime("%Y-%m-%d %H:%M (UTC%z)")


def alert_for(error: BaseException, when: datetime) -> SyncAlert:
    if isinstance(error, GarminReauthRequired):
        return SyncAlert(
            REAUTH_TITLE,
            f"{error}, so the roughly annual interactive login is due. Complete it on the"
            f" home machine; the sync stays stopped until you do.\nFound {local_time(when)}.",
            # Only this one waits on the athlete, so only this one interrupts them.
            priority="urgent", tags="key")
    if isinstance(error, SyncSignInRequired):
        # The two sign-ins fall due independently and are renewed differently, so the
        # message names which one rather than sending the athlete to the wrong place.
        return SyncAlert(
            COACH_REAUTH_TITLE,
            f"{error}. Run the sync's sign-in on the home machine; nothing reaches the"
            f" coach until you do.\nFound {local_time(when)}.",
            priority="urgent", tags="key")
    return SyncAlert(FAILURE_TITLE, f"{error}\nStopped {local_time(when)}.",
                     priority="high", tags="warning")


def post_to_ntfy(url: str, headers: dict[str, str], body: bytes) -> None:
    with urlopen(Request(url, data=body, method="POST", headers=headers), timeout=30) as response:
        if response.status >= 300:
            raise RuntimeError(f"Notification service returned HTTP {response.status}")


class NtfyNotifier:
    """Sends a SyncAlert to one ntfy topic."""

    def __init__(
        self,
        server: str,
        topic: str,
        token: str | None = None,
        send: Callable[[str, dict[str, str], bytes], None] = post_to_ntfy,
    ) -> None:
        if not server.startswith("https://"):
            raise ValueError("notification server must use HTTPS; the topic travels in the URL")
        if not topic:
            raise ValueError("notification topic must not be empty")
        self._url = f"{server.rstrip('/')}/{topic}"
        self._topic = topic
        self._token = token
        self._send = send

    def redacted(self, text: str) -> str:
        """Whatever is said about a send, said without naming the topic."""
        return text.replace(self._topic, "<topic>")

    def __call__(self, alert: SyncAlert) -> None:
        headers = {"Title": alert.title, "Priority": alert.priority, "Tags": alert.tags,
                   "Content-Type": "text/plain; charset=utf-8"}
        if self._token:
            headers["Authorization"] = f"Bearer {self._token}"
        try:
            self._send(self._url, headers, alert.body.encode("utf-8"))
        except Exception as error:
            # Whatever the service says may quote the URL, and the URL carries the topic.
            raise RuntimeError(self.redacted(f"{error}")) from None


def notifier_from_environment(
    environ: Mapping[str, str] | None = None,
    warn: Callable[[str], None] = lambda text: print(text, file=sys.stderr),
    send: Callable[[str, dict[str, str], bytes], None] = post_to_ntfy,
) -> Callable[[SyncAlert], None]:
    """The home machine's notification settings. A machine whose settings are missing
    or unusable still syncs, and says so: training data is worth more than the message
    the run may never need to send."""
    environ = os.environ if environ is None else environ
    topic = environ.get("COACH_NTFY_TOPIC")
    if not topic:
        warn("No COACH_NTFY_TOPIC is configured, so a failed run will reach no phone.")
        return lambda alert: None
    try:
        return NtfyNotifier(environ.get("COACH_NTFY_SERVER") or DEFAULT_SERVER, topic,
                            environ.get("COACH_NTFY_TOKEN"), send)
    except ValueError as unusable:
        warn(f"{unusable}, so a failed run will reach no phone.")
        return lambda alert: None


def run_notifying_failures(
    sync: Callable[[], Any],
    notify: Callable[[SyncAlert], None],
    now: Callable[[], datetime] = lambda: datetime.now().astimezone(),
    warn: Callable[[str], None] = lambda text: print(text, file=sys.stderr),
) -> int:
    """Run the sync, sending one SyncAlert if it gives up; returns an exit code."""
    try:
        sync()
    except Exception as error:
        alert = alert_for(error, now())
        warn(f"{alert.title}: {alert.body}")
        try:
            notify(alert)
        except Exception as undelivered:
            # A phone that was not reached is not a reason to lose the failure itself.
            warn(f"The notification could not be sent: {undelivered}")
        return 1
    return 0
