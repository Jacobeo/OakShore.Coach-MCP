# 07: Failure notification and re-authentication prompt

**What to build:** When a run gives up, the athlete's phone tells them, and the
message comes from the home machine. When the annual interactive login falls due,
that is a distinct message saying what to do, not the same alarm as a failure.

**Blocked by:** 01 Activities into the store, safely.

**Status:** done

**Source:** [Garmin sync](../../03-garmin-sync.md), [ADR 0001](../../../docs/adr/0001-sync-runs-at-home.md).

- [x] A run that aborts, or that exhausts its retries, sends a push notification
  from the home machine naming what failed and when. Nothing is sent from Azure,
  which cannot tell a failed sync from a machine that is switched off.
- [x] An authentication failure that requires an interactive login produces a
  distinct message identifying it as the annual re-login, so it is not mistaken
  for a transient fault.
- [x] A transient failure that succeeds on retry produces no notification.
- [x] Notification settings come from the home machine's configuration, and the
  topic is handled as a secret and kept out of logs and the repository.
- [x] Each case is asserted at the Python seam without sending a real message.

**Built as:** [notify.py](../../../src/sync/notify.py), a `SyncAlert` and an
`NtfyNotifier` behind an injected sender, wired into `main` by
`run_notifying_failures`. `GarminReauthRequired` in
[garmin_client.py](../../../src/sync/garmin_client.py) is the one type that
separates the annual login from a fault; it subclasses `GarminAbort`, so 401
still aborts without retrying. Asserted at the Python seam in
[test_sync_notifications.py](../../../tests/python/test_sync_notifications.py).

**Judgment calls**, open to correction:

- ntfy, configured only by `COACH_NTFY_TOPIC`, `COACH_NTFY_SERVER` and
  `COACH_NTFY_TOKEN`, matching the existing `COACH_*` convention. No config file,
  so no topic can reach the repository.
- A machine with no topic configured warns at startup and syncs anyway, rather
  than refusing to start, so a manual run is never blocked by notification
  settings. The risk accepted is a scheduled run sitting silently misconfigured.
- One exit code, 1, for every failure. The failure and the annual login are
  distinguished in the message, which is what this ticket asks for; ticket 08 can
  add a distinct code if the scheduler turns out to need one.
