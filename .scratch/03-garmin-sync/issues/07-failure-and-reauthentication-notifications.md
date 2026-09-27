# 07: Failure notification and re-authentication prompt

**What to build:** When a run gives up, the athlete's phone tells them, and the
message comes from the home machine. When the annual interactive login falls due,
that is a distinct message saying what to do, not the same alarm as a failure.

**Blocked by:** 01 Activities into the store, safely.

**Status:** ready-for-agent

**Source:** [Garmin sync](../../03-garmin-sync.md), [ADR 0001](../../../docs/adr/0001-sync-runs-at-home.md).

- [ ] A run that aborts, or that exhausts its retries, sends a push notification
  from the home machine naming what failed and when. Nothing is sent from Azure,
  which cannot tell a failed sync from a machine that is switched off.
- [ ] An authentication failure that requires an interactive login produces a
  distinct message identifying it as the annual re-login, so it is not mistaken
  for a transient fault.
- [ ] A transient failure that succeeds on retry produces no notification.
- [ ] Notification settings come from the home machine's configuration, and the
  topic is handled as a secret and kept out of logs and the repository.
- [ ] Each case is asserted at the Python seam without sending a real message.
