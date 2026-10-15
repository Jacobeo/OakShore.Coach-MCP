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

### Daily wellness

A second run, `20261008T103754Z`, captured by
[`spikes/wellness-capture/`](../../spikes/wellness-capture/README.md) on
2026-10-08 for 2026-09-19 to 2026-09-21, the last days of the window above, and
for a 2010-01-01 sentinel from before the account existed — Garmin's answer for
a day holding nothing.

| File | Endpoint |
| --- | --- |
| `wellness-service--dailySleepData__date-<date>.json` | `/wellness-service/wellness/dailySleepData/<displayName>` — 4 files |
| `usersummary-service--daily__date-<date>.json` | `/usersummary-service/usersummary/daily/<displayName>` — 4 files |
| `wellness-service--bodyBattery-reports-daily__date-2026-09-21.json` | `/wellness-service/wellness/bodyBattery/reports/daily` — evidence for [ADR 0010](../../docs/adr/0010-wellness-reads-sleep-and-the-daily-summary.md); the sync does not call it |
| `wellness-service--dailyStress__date-2026-09-21.json` | `/wellness-service/wellness/dailyStress` — the same |
| `index-wellness.json` | that run's index, with each response's status; the four HRV 204s are recorded here and nowhere else ([ADR 0011](../../docs/adr/0011-this-watch-records-no-hrv.md)) |

These carry the account id under `userProfilePK`, `userProfilePk`,
`userProfileId` and `userDailySummaryId`, plus `displayName`, per-day `uuid`s
and `deviceId`s, at any depth. [`scrub_wellness.py`](../../spikes/wellness-capture/scrub_wellness.py)
replaces them with the placeholders the Activity list uses, refuses any file its
rules do not name, and publishes nothing if a value from the athlete's social
profile survives anywhere. The social profile itself is never published.
Everything else, physiological figures and timestamps included, is as Garmin
sent it, the same decision the Activity list made.

### Daily form

A third run, `20261015T070639Z`, by the same capture with `--plan
training-status` on 2026-10-15: the training status payload for 2026-09-19 to
2026-09-21 and the 2010-01-01 sentinel. Each of the three days is dated to the
day requested, with its own weekly load and timestamp, which is what
[ADR 0012](../../docs/adr/0012-daily-form-is-one-training-status-call.md)'s
past-date reading rests on. The sentinel is Garmin's answer for a day holding
nothing: 200, with every member null but the account id.

| File | Endpoint |
| --- | --- |
| `metrics-service--trainingstatus-aggregated__date-<date>.json` | `/metrics-service/metrics/trainingstatus/aggregated/<date>` — 4 files |
| `index-training-status.json` | that run's index |

These carry the account id under `userId` and the device id under `deviceId`
and as the key of `latestTrainingStatusData`. The scrubber replaces all three
with placeholder 1, and publishes nothing if the payload names more than one
device, which would collapse two watches onto one placeholder. No social
profile is fetched for this run, so the raw values the rules replaced are what
it sweeps for.

What the payloads actually say, field by field, is in
[findings.md](../../spikes/strength-data-spike/findings.md). Read that before
writing code against these files — several fields do not mean what their names
suggest, and `weight` in particular carries two non-gram values that are not
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
