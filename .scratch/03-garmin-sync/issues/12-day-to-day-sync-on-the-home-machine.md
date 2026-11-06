# 12: The day-to-day sync, set up and proven on the home machine

**What to build:** The nightly sync runs for real on the athlete's home machine:
every credential obtained, the scheduled task registered, and the first
unattended runs observed reaching the hosted store and the chat client.
Tickets 01–08 built and documented each piece, but asserted them only against
fakes. None has been done end to end against the live services.

**Blocked by:** None (can start immediately). It runs alongside ticket 11, not
after it.

**Status:** ready-for-human

**Source:** [Garmin sync](../../03-garmin-sync.md), [ADR 0001](../../../docs/adr/0001-sync-runs-at-home.md),
[ADR 0006](../../../docs/adr/0006-garmin-token-file-location.md),
[ADR 0013](../../../docs/adr/0013-the-sync-holds-its-own-workos-sign-in.md);
README sections "Watermark runs", "Failure notifications" and "Scheduled and
manual runs".

## Gaps found before writing this ticket

- **The Garmin login has no supported entry point.** The README says a missing
  or expired Garmin token "requires the interactive login flow from the
  strength-data spike", and the annual-login notification sends the athlete
  there. Spikes are never imported (AGENTS.md). The sync itself only loads a
  saved token.
- **The WorkOS sign-in has never run live.** Ticket 08 records that the sync's
  OAuth client does not exist in WorkOS yet. It also leaves unproven whether an
  access token issued from the saved refresh token carries an audience ingest
  accepts.
- **The scheduled task has never been registered**, and no real notification has
  ever been sent.
- **The setup steps are spread across five README sections**, with no single
  order to follow on a fresh machine.

## Code

- [ ] The sync has its own interactive Garmin login, MFA included, that saves
  the token where ADR 0006 puts it. It takes the SyncSlot, as `--login` does. The
  README and the annual-login notification point to it, not to the spike.
  Asserted at the Python seam with no live Garmin call.

## Machine setup

- [ ] The README has one ordered checklist for a fresh machine. Each step links to
  its existing section rather than repeating it: install Python and the
  requirements, the Garmin login, register the WorkOS client, `--login`, the
  user environment variables (`COACH_INGEST_URL`, `COACH_SYNC_EARLIEST_DATE`,
  `COACH_INGEST_AUTHORITY`, `COACH_INGEST_CLIENT_ID`, `COACH_NTFY_TOPIC`),
  register the task, and confirm the first run.
- [ ] The hosted API is on the latest image, confirmed by the active revision's
  digest, before the first run. Ticket 11's production run makes the same check;
  whichever ticket deploys first does it for both.
- [ ] `COACH_SYNC_EARLIEST_DATE` is chosen to meet the end of the ExportArchive,
  so the sync and the ExportImport together leave no gap. Overlapping is harmless,
  since data read from the API wins (ADR 0014).

## Proven live

- [ ] The Garmin login and `--login` both complete, and a manual
  `run-sync.ps1` posts to the hosted ingest with a token issued from the saved
  sign-in. This settles ticket 08's open question about the audience.
- [ ] The scheduled task is registered. A scheduled run, including one after the
  machine was off at 21:30, completes unattended, and `get_sync_status` in a
  chat client shows its sync time.
- [ ] A test message reaches the phone through the configured ntfy topic.
- [ ] Weeks with the machine off do not cost a sign-in. Two settings are read,
  recorded and set long enough: WorkOS's session and refresh-token lifetime,
  including any inactivity timeout, and Garmin's token lifetime. The README states
  the longest the machine can be off before a sign-in is due. Beyond that limit,
  the run sends the matching sign-in notification and loses no data.
- [ ] After a week of nightly runs, the results are recorded below as a comment:
  missed nights caught up, any failures and their notifications, and the
  Watermark's position.
