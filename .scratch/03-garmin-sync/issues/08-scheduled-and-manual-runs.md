# 08: Scheduled and manual runs on the home machine

**What to build:** The sync runs itself each evening, catches up on its own after
the machine has been off for a few days, and can still be triggered by hand right
after a session without the athlete having to think about whether that is safe.

**Blocked by:** 02 Watermark, bounded and resumable runs; 07 Failure notification
and re-authentication prompt.

**Status:** ready-for-agent

**Source:** [Garmin sync](../../03-garmin-sync.md).

- [ ] A documented, repeatable registration creates the daily scheduled task,
  configured to run a missed occurrence as soon as the machine is next available.
- [ ] A scheduled run and a manual run use the same entry point, and a manual run
  at any time of day is safe and changes nothing if there is nothing to fetch.
- [ ] Two runs that overlap cannot corrupt the watermark or post a day twice: the
  second waits or exits rather than proceeding.
- [ ] The README documents registration, manual invocation, and how to confirm
  the last successful run from a chat client.

## Comments

2026-09-30: Measured during the ticket 02 live smoke: WorkOS access tokens for
the ingest audience expire after five minutes. A scheduled run therefore cannot
use a manually obtained token, and a long catch-up could outlive a single one.
This ticket needs an unattended credential path on the home machine (a refresh
token or machine-to-machine client) before scheduling can work.
