# Strength data spike — dump harness

Throwaway code for [spec 01](../../.scratch/01-strength-data-spike.md). It
answers one question — do this athlete's on-watch weight and exercise
corrections survive into the Garmin API — and validates the cardio path at the
same time, so that is not left as a second unknown. Its durable output is the
files under `dumps/`, not this script.

**Do not grow this into the sync.** Spec 03 is a separate program with its own
watermark, ceiling and ingest contract; the only thing it inherits from here is
the token file location.

## Running it

From the home machine, over the residential connection. Never from Azure and
never from a container on a cloud host ([ADR 0001](../../docs/adr/0001-sync-runs-at-home.md)).

```bash
uv venv .venv
uv pip install --python .venv/Scripts/python.exe -r requirements.txt
.venv/Scripts/python.exe dump_garmin.py --days 48
```

The first run asks for the Garmin email, the password (not echoed, not stored)
and the MFA code. Every run after that reuses the token and prints
`no credentials, no MFA prompt`. If a run ever asks for credentials again, the
token has gone stale — delete `~/.garminconnect` and log in by hand once more.

| Flag | Default | |
| --- | --- | --- |
| `--days` | `14` | Window length, counted back from today |
| `--pace` | `4.0` | Seconds between calls; below 3 is refused |
| `--token-dir` | `$GARMINTOKENS`, else `~/.garminconnect` | |
| `--out` | `./dumps` | |
| `--field-check` | `./activity-list-field-check.md` | |
| `--coverage` | `./exercise-set-coverage.md` | |
| `--zone-coverage` | `./heart-rate-zone-coverage.md` | |
| `--from-dumps` | off | Rebuild the notes from `--out`, with no Garmin call |

One run is one list call per page, then the zone-boundaries call once, then one
`exerciseSets` call per strength Activity and one `hrTimeInZones` call per
Activity that is not. Forty-eight days is the window the three notes beside
this README describe — 15 Activities, 7 of them strength and 8 cardio, so
seventeen paced calls — and `--days 48` is what reproduces it. The notes are
rewritten in full by every run, so a narrower window narrows what they claim;
the default fourteen days holds one strength Activity.

The boundaries call goes before the per-Activity passes on purpose: it is one
cheap call, and an abort part way through the long passes still leaves it on
disk. Without it a `hrTimeInZones` dump is seconds against zone numbers that
mean nothing.

Fourteen days is enough to catch a few Activities of each type, which is all
the spike needs from a first look. If `activity-list-field-check.md` comes back
saying there were no strength Activities in the window, widen it — `--days 90`
is what [step 1 of the build order](../../docs/build-order.md) sketched — at
the cost of more pages and so more paced calls.

Every strength Activity dumped followed a Workout pushed to the watch, because
that is how this athlete trains. The degraded case research warns about —
accelerometer-inferred, `weight` null, Exercise category `UNKNOWN` — is not in
this history to find.

## Where the token lives

`%USERPROFILE%\.garminconnect\garmin_tokens.json`, for the reasons in
[ADR 0006](../../docs/adr/0006-garmin-token-file-location.md). Spec 03 reads
the same file.

## What it produces

`activity-list-field-check.md`, beside this README: whether the list entry
carries `totalSets`, `activeSets` and `totalReps`, so spec 03 knows whether it
can skip the per-Activity summary call. It is tracked, and carries no Activity
identifiers so that it can be. Whether the sync needs `exerciseSets` on top of
those totals is a separate question, settled in
[ADR 0007](../../docs/adr/0007-prescription-needs-per-exerciseset-detail.md).

`exercise-set-coverage.md`, also tracked and also identifier-free: which
strength Activities in the window had their `exerciseSets` response dumped, how
many rows each dump holds, and whether the Activity followed a Workout or was
freestyle. What the payloads actually say is issue 04's question, not this
note's.

`heart-rate-zone-coverage.md`, tracked as well: which cardio Activities had
their `hrTimeInZones` response dumped, which sports the window covers, which
sport profiles the zone-boundaries call returned, and which profile's
boundaries each Activity was actually scored against. The boundaries in bpm are
left in the dumps rather than repeated here, for the same reason the Activity
identifiers are.

Everything else lands in `dumps/<run-timestamp>/`. One directory per run, so a
later run over a shifted window cannot overwrite an earlier Activity's bytes —
re-fetching is the thing the spike exists to avoid. `--from-dumps` reads the
most recent run, and takes the window from its `index.json` rather than from
`--days`.

- `activitylist-service--search-activities__page-NN.json` — one file per page
  of the Activity list, exactly the bytes Garmin sent. Nothing is parsed into
  them and nothing is reformatted. Every response is written before anything
  reads it, so a payload that breaks the parser is on disk rather than lost.
- `activity-service--exerciseSets__activity-<id>.json` — one file per strength
  Activity, again exactly the bytes Garmin sent, written before anything reads
  them.
- `activity-service--hrTimeInZones__activity-<id>.json` — the same, one file
  per Activity that is not a strength Activity.
- `biometric-service--heartRateZones.json` — the configured zone boundaries.
  One file per run, not one per Activity: the boundaries are per athlete and
  per sport profile, so the call is made exactly once.
- `exercise-set-cases.md` — which of those dumps followed a Workout and which
  was freestyle, by Activity. It lives here rather than beside this README
  because it names Activities, and identifiers are exactly what issue 04's
  scrubbing decision has to weigh before anything is committed.
- `index.json` — which endpoint, which query parameters and which Activity each
  file came from, plus when it was fetched. The query parameters and fetch time
  are only knowable at fetch time, which is why they are captured here rather
  than reconstructed later.
- `ABORT-<status>-<timestamp>.txt` — written only when a run stops on an error.

`dumps/` is gitignored. The payloads are real personal training data, and the
findings note in spec 01 decides what (if anything) is scrubbed before any of
it is committed as a fixture corpus.

## Safety properties, and how they are enforced

- **No Garmin write of any kind.** `PacedReader` can only issue `GET`. There is
  no code path to a `PUT`, `POST` or `DELETE`, and in particular none to
  `set_activity_exercise_sets`, whose replace-all semantics over recorded
  history are why [ADR 0004](../../docs/adr/0004-garmin-writes-are-additive-only.md)
  exists.
- **At least a few seconds between calls.** Four by default, including between
  login and the first list call, and between every Activity of the
  `exerciseSets` pass.
- **Stop, never retry.** Any error status ends the run and reports the status,
  `Retry-After` and the response body. Retrying a rate limit extends the block
  ([ADR 0005](../../docs/adr/0005-bounded-resumable-sync.md)), so 401, 403 and
  429 are called out by name in the report — and no other error status is
  retried either. An abort part way through either per-Activity pass leaves
  the remaining Activities unfetched and the three notes untouched;
  `--from-dumps` rebuilds them from whatever landed.
- **Four endpoints, and no others.** `ACTIVITY_LIST_PATH`,
  `EXERCISE_SETS_PATH`, `HR_TIME_IN_ZONES_PATH` and `HEART_RATE_ZONES_PATH` in
  `dump_garmin.py` are every path the script can ask for. The per-Activity
  *summary* endpoint is deliberately not among them: the list entry already
  carries the strength totals and the per-Exercise rollup
  ([ADR 0007](../../docs/adr/0007-prescription-needs-per-exerciseset-detail.md)).

The spike is otherwise untested by design. Pacing and the abort rule are the
exception — across the list pass and across both per-Activity passes — because
provoking them by hand means provoking a real block. GET-only is tested for the
same reason: demonstrating it means risking a real write. Two more earn their
place beside them: that the cardio pass never reaches for the zone boundaries,
so one fetch of them stays one call; and that two sport profiles configured
with identical floors are reported as ambiguous rather than silently resolved
to one — this athlete has a single profile, so that branch cannot be provoked
from their data at all.

```bash
.venv/Scripts/python.exe -m pytest
```

## Reading the bytes without a parser

`PacedReader` reads from the authenticated session under `Garmin.client`
rather than calling `Garmin.connectapi`, which returns parsed JSON and retries
on its own. Three attributes private to `python-garminconnect` are touched to
do that: `_api_session`, `_connectapi` and `get_api_headers`. It is a
deliberate trade for a throwaway script that needs the response bytes and the
exact status code; a long-lived component should not copy it, and spec 03
should decide its own approach rather than inherit this one.

## One convention deliberately not followed

[AGENTS.md](../../AGENTS.md) requires every tracked file to be added to
`coach-mcp.sln` in the same change. No solution file exists yet, and AGENTS.md
puts its creation with the change that creates `src/` — so these files are on
disk and not in a solution. Whoever creates the solution should add `spikes/`
and `docs/adr/` as solution folders then.
