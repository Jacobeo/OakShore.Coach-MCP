# Garmin fixture corpus

Real Garmin Connect responses, captured by
[spec 01's dump harness](../../spikes/strength-data-spike/README.md) and scrubbed
of identifying fields before being committed. This is the durable output of the
strength data spike, and the fixture corpus for both test seams described in
[.scratch/00-README.md](../../.scratch/00-README.md):

- the **Python seam** replays these files at the Garmin client boundary
- the **.NET seam** derives its ingest batches from them

It lives at the repository root rather than under `src/` or `tests/` because
neither runtime owns it and both read it.

## Provenance

| | |
| --- | --- |
| Run | `20260922T090450Z` |
| Fetched | 2026-09-22 |
| Window | 2026-08-05 to 2026-09-22 (48 days) |
| Activities | 15 — 7 strength, 7 running, 1 indoor cycling |
| Athlete | one; every strength Activity followed a Workout pushed to the watch |

`index.json` is the harness's own record: for every file, the endpoint path, the
query parameters, the Activity it belongs to, its size and when it was fetched.
Read it rather than inferring provenance from file names.

## What is here

| File | Endpoint |
| --- | --- |
| `activitylist-service--search-activities__page-00.json` | `/activitylist-service/activities/search/activities` |
| `activity-service--exerciseSets__activity-<id>.json` | `/activity-service/activity/<id>/exerciseSets` — 7 files, one per strength Activity |
| `activity-service--hrTimeInZones__activity-<id>.json` | `/activity-service/activity/<id>/hrTimeInZones` — 8 files, one per cardio Activity |
| `biometric-service--heartRateZones.json` | `/biometric-service/heartRateZones` — once per run, not per Activity |
| `index.json` | the harness's index, not a Garmin response |
| `exercise-set-cases.md` | which `exerciseSets` dump followed a Workout and which was freestyle, by Activity |

What the payloads actually say, field by field, is in
[findings.md](../../spikes/strength-data-spike/findings.md). Read that before
writing code against these files — several fields do not mean what their names
suggest, and `weight` in particular carries two sentinel values that are not
weights.

## What was changed, and what was not

The three per-Activity payload types — `exerciseSets`, `hrTimeInZones` and
`heartRateZones` — carry no identifying field and are **the bytes Garmin sent,
byte for byte**. So are `index.json` and `exercise-set-cases.md`.

Only the Activity list was modified. Its account, profile, device, free-text and
location fields were replaced with stable placeholders by
[`scrub_dumps.py`](../../spikes/strength-data-spike/scrub_dumps.py); everything
else, including `activityId`, `workoutId`, every timestamp and every metric, is
untouched. The full rule table and the reasoning behind each line — including
what was deliberately kept — is in
[findings.md](../../spikes/strength-data-spike/findings.md#what-was-scrubbed-and-what-was-not).

Placeholders are stable rather than random, so joins across files still hold and
re-running the scrubber produces the same bytes.

## Regenerating it

From the spike directory, against a raw run under its gitignored `dumps/`:

```bash
.venv/Scripts/python.exe scrub_dumps.py dumps/20260922T090450Z ../../fixtures/garmin
```

The scrubber stages its output and publishes nothing unless every file is a kind
its rules cover, an Activity list is present to check against, and every scrubbed
field holds what the rule table says. So a payload that changes shape under it, or
a file it was not written for, is a failed run rather than a leak.
