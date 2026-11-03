# 08: Scheduled and manual runs on the home machine

**What to build:** The sync runs itself each evening, catches up on its own after
the machine has been off for a few days, and can still be triggered by hand right
after a session without the athlete having to think about whether that is safe.

**Blocked by:** 02 Watermark, bounded and resumable runs; 07 Failure notification
and re-authentication prompt.

**Status:** done

**Source:** [Garmin sync](../../03-garmin-sync.md).

- [x] A documented, repeatable registration creates the daily scheduled task,
  configured to run a missed occurrence as soon as the machine is next available.
- [x] A scheduled run and a manual run use the same entry point, and a manual run
  at any time of day is safe and changes nothing if there is nothing to fetch.
- [x] Two runs that overlap cannot corrupt the watermark or post a day twice: the
  second waits or exits rather than proceeding.
- [x] The README documents registration, manual invocation, and how to confirm
  the last successful run from a chat client.

## Comments

2026-09-30: Measured during the ticket 02 live smoke: WorkOS access tokens for
the ingest audience expire after five minutes. A scheduled run therefore cannot
use a manually obtained token, and a long catch-up could outlive a single one.
This ticket needs an unattended credential path on the home machine (a refresh
token or machine-to-machine client) before scheduling can work.

2026-10-27: Settled by [ADR 0013](../../../docs/adr/0013-the-sync-holds-its-own-workos-sign-in.md).
The sync signs in to WorkOS once per machine and keeps the refresh token, issuing
its own access tokens from it. A machine-to-machine client was the better-shaped
idea but WorkOS documents M2M applications as third-party only and does not
document how to make such a token's `aud` match the ingest audience; betting the
ticket on that was not worth it. A shared ingest secret was rejected for adding a
second authentication path to a health-data endpoint.

**Built as:** [sync_sign_in.py](../../../src/sync/sync_sign_in.py), a `SyncSignIn`
that exchanges the saved refresh token for access tokens and renews them mid-run,
and a `sign_in` running the one interactive authorization-code-with-PKCE flow
behind an injected exchange, browser and redirect catcher.
[sync_slot.py](../../../src/sync/sync_slot.py) holds the machine's one SyncSlot as
an operating-system lock, wired in by `run_unless_already_running` in
[sync_activities.py](../../../src/sync/sync_activities.py) outside the
notification wrapper, so an overlap is logged rather than sent to a phone.
[run-sync.ps1](../../../src/sync/run-sync.ps1) is the one entry point the
scheduler and the athlete both use;
[register-scheduled-task.ps1](../../../src/sync/register-scheduled-task.ps1)
registers it daily with `-StartWhenAvailable`. Asserted at the Python seam in
[test_sync_sign_in.py](../../../tests/python/test_sync_sign_in.py) and
[test_sync_slot.py](../../../tests/python/test_sync_slot.py). `SyncSlot` and
`SyncSignIn` are named in [CONTEXT.md](../../../CONTEXT.md), and `SyncAlert` there
was updated to the three messages it now carries.

**Found by review and fixed** (two axes, standards and spec, against `main`):

- **The scheduled task restarted a rate-limited run.** `-RestartCount 2
  -RestartInterval 15m` meant Windows would re-run a sync that had exited non-zero
  after a 403 or 429, contacting Garmin twice more inside the block the circuit
  breaker had just backed off from, and sending three alerts. Removed; a failed
  run now waits for tomorrow, which the Watermark makes free.
- **Two paths could rotate the refresh token without saving it**, which costs an
  interactive sign-in. A response carrying no replacement now keeps the saved one
  rather than raising on a missing key, and an unreadable 200 says so in a message
  naming the risk. Both are regression-tested.
- **`--login` ran outside the SyncSlot**, so a sign-in could race a running sync's
  rotation write to the same file. It takes the slot now and exits 1 asking the
  athlete to wait. `atomic_file` also scratches to a per-process name, so two
  writers cannot swap halves of a file.
- **A run without dates needs `COACH_SYNC_EARLIEST_DATE`**, which the new README
  section did not name. A scheduled run missing it exits 2 with nobody watching.
  Named now, with `COACH_INGEST_URL`, as required before registering the task.
- Naming, glossary and comment-rule fixes: `SyncSlot` carries into `--slot-file`
  and `COACH_SYNC_SLOT`; `IngestCredential` became `SyncSignIn` to match the
  glossary entry added for it; three module docstrings that re-derived the design
  were cut to a line and a pointer to ADR 0013; a stale comment in
  `watermark.py` and a `Callback` class attribute holding state between requests
  both went.

**Judgment calls**, open to correction:

- **The second run exits rather than waits.** The ticket allows either. Waiting
  would hang a manual run for the length of a thirty-day catch-up with no
  feedback; exiting loses nothing, because the Watermark means the run already
  going covers the same days.
- **An overlap sends no notification and exits 0.** It is the design working, not
  a fault, and paging the athlete for it would teach them to ignore the one
  message that matters. A blocked `--login` does exit 1, because there the athlete
  is sitting at the machine waiting for a browser.
- **Only `invalid_grant` means sign in again.** Every other answer from the token
  endpoint is a fault, so it retries and reads as a failure. A 400 for some other
  reason would otherwise send the athlete to a sign-in page that fixes nothing.
- **Settings come from user environment variables, not the task definition**,
  which is readable on the machine. The scheduled task stores no secret and no
  password.
- **The resource indicator is kept though unused by default.** Review called it
  speculative. It stays because the need is real and named: whether WorkOS honours
  `resource` on a token request is undocumented, and if the registered default
  Resource Indicator turns out not to be the ingest audience, this is the
  difference between an environment variable and a code change on a machine the
  athlete is not sitting at.
- **No config object for the sign-in's four settings.** Review called them a data
  clump. They travel to two call sites and are read from four distinct CLI or
  environment sources, so bundling them would move the unpacking rather than
  remove it.
- **The token endpoint is the same Python seam, not a new one.** The spec names
  the Garmin client boundary, and this is the same boundary one host over: the
  tests assert on the requests made and the header the batches carry, never on
  internals. No new seam was introduced, so AGENTS.md's "stated reason" does not
  apply — but it is stated here.
- `atomic_file.py` was extracted so the sign-in and the Watermark share one
  write-then-replace, including the Windows retry; the Watermark's own test moved
  with it.

**Not verified live.** No live sign-in was performed: the OAuth client does not
exist in WorkOS yet, and registering it — with PKCE, `offline_access` and the
loopback redirect — is the athlete's step. The exchange and sign-in flows are
asserted against a fake token endpoint only, so the one thing still unproven is
whether a WorkOS refresh token yields an access token whose `aud` the ingest
endpoint accepts. The scheduled task was not registered on this machine; its
settings and trigger were checked against the real cmdlets without registering,
and both scripts parse clean. The overlap was checked through the real entry
point, where a second sync exits 0 and a blocked `--login` exits 1, neither
contacting Garmin.
