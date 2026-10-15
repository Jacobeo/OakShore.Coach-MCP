# 06: Daily form

**What to build:** For each day synced, the sync also stores the systemic
TrainingLoad — Garmin's weekly training load against its load tunnel — and the
VO2max estimates the same training status payload carries, so the athlete's
current form can be assessed alongside what they did. One call per day of data.

**Blocked by:** 05 Daily wellness.

**Status:** done

**Source:** [Garmin sync](../../03-garmin-sync.md),
[ADR 0009](../../../docs/adr/0009-trainingload-has-two-axes.md),
[ADR 0012](../../../docs/adr/0012-daily-form-is-one-training-status-call.md).

- [x] First, fixtures. The capture spike (`spikes/wellness-capture/`) and its
  scrubber are extended to `/metrics-service/metrics/trainingstatus/aggregated/{date}`,
  and one paced capture fetches 2026-09-19 to 2026-09-21 and the 2010-01-01
  sentinel — four calls — whose scrubbed responses join `fixtures/garmin/`. The
  account id and the device id, which also keys `latestTrainingStatusData`,
  never reach the repository. If any response is not dated to the day requested,
  work stops and the ticket returns to triage: ADR 0012's past-date reading rests
  on one date.
- [x] For each day within a run's bounds, the training status payload is fetched
  inside the per-day loop and posted with that day as a version 4 batch. No
  TrainingReadiness, VO2max (`maxmet`), load-focus or FTP call is made. A day
  costs four calls — the Activity list, sleep, the daily summary and training
  status — within the per-run ceiling, and the Python seam asserts their number
  and order for a multi-day run.
- [x] The systemic TrainingLoad is stored per day as the weekly training load and
  the load tunnel's lower and upper bounds, verbatim from the payload, per ADR
  0009. Nothing is recomputed. The `trainingStatus`, `loadLevelTrend` and
  `fitnessTrend` codes are kept only in the archived response, arrays removed, as
  [ADR 0010](../../../docs/adr/0010-wellness-reads-sleep-and-the-daily-summary.md)
  archives wellness.
- [x] VO2max is read from the payload's `mostRecentVO2Max` and stored per sport,
  generic and cycling, under the date Garmin computed it — which may precede the
  batch's range but never follow it. Re-reading an unchanged estimate adds
  nothing.
- [x] Version 4 states that every date in the range was synced for daily form
  too: a day Garmin has no training status for is posted empty and completed. A
  day missing VO2max stores its TrainingLoad rather than failing, and an absence
  is distinguishable from a zero.
- [x] A payload whose `latestTrainingStatusData` holds more than one device stops
  the run with a clear error rather than choosing a watch to trust.
- [x] `get_sync_status` reports `TrainingLoad` (days stored) and `VO2max`
  (estimates stored) collections with their last sync times, and
  `latestWellnessDay.holds` lists `TrainingLoad` when that date has one. No value
  is returned; `get_current_form` in build-order step 6 is where they first
  appear. Asserted at the .NET seam.

## Comments

2026-10-08: Probed before planning, from ticket 05's capture spike
(`spikes/wellness-capture/capture_wellness.py --plan form`, run
`20261008T111226Z`): four paced GETs for 2026-09-21. The raw responses stay in
the gitignored `dumps/` on the home machine; none is in the fixture corpus yet.

- `/metrics-service/metrics/trainingreadiness/{date}` returned `[]`. The watch,
  a Forerunner 245, produces no TrainingReadiness; Garmin derives it partly from
  HRV status, which this watch also lacks
  ([ADR 0011](../../../docs/adr/0011-this-watch-records-no-hrv.md)).
- `/metrics-service/metrics/trainingstatus/aggregated/{date}` carries a training
  status, but `acuteTrainingLoadDTO` is null: no acute load, chronic load, ratio
  or ratio status. What it does carry is `weeklyTrainingLoad` (140) with a load
  tunnel (`loadTunnelMin` 67, `loadTunnelMax` 218) and `trainingStatus`,
  `loadLevelTrend` and `fitnessTrend` codes.
  `mostRecentTrainingLoadBalance.metricsTrainingLoadBalanceDTOMap` is null: no
  load-focus buckets.
- `/metrics-service/metrics/maxmet/daily/{date}/{date}` returned `[]` for the
  day, but the training status payload's `mostRecentVO2Max` carries generic 39.3,
  dated 2026-09-02, and cycling 32.0, dated 2026-08-30. VO2max exists, dated by
  when Garmin last computed it rather than per day.
- `/biometric-service/biometric/latestFunctionalThresholdPower/CYCLING` returned
  every field null: no FTP is set.

So on this watch TrainingReadiness, the systemic axis's acute, chronic and ratio
fields that [ADR 0009](../../../docs/adr/0009-trainingload-has-two-axes.md)
assumed, the load-focus buckets and the cycling FTP have nothing to store. Status
moved to needs-triage until that is decided.

2026-10-15: Triaged with the athlete against the probe above. Decisions: the
systemic axis is the weekly load against its tunnel, and ADR 0009 is amended to
match; TrainingReadiness, the acute:chronic load and ratio, load focus and FTP
are dropped (ADR 0012) and TrainingReadiness leaves CONTEXT.md; VO2max comes off
the training status payload, kept per sport under Garmin's date, and CONTEXT.md
gains the term; one call per day of data, because a past date returns that
date's figures and a catch-up after days away therefore still builds a true
history; fixtures are this ticket's first step, with the dating check; the codes
stay archive-only until their meaning is known; version 4 carries daily form;
and `get_sync_status` reports counts and freshness only. The rule for more than
one device in the payload is the agent's call, accepted by the athlete. Spec 04's
"Current form" paragraph, the build order and `docs/example-session.md` now say
what this watch does not produce.

2026-10-15: Fixtures first. The capture spike gained `--plan training-status`
and its scrubber training status runs, and run `20261015T070639Z` made four
paced GETs with no refusal. 2026-09-19, -20 and -21 each came back dated to the
day requested, with weekly loads of 135, 135 and 140 and their own timestamps,
so ADR 0012's past-date reading held and work went on. The sentinel answered
200 with every member null but the account id, VO2max included. The scrubbed
responses and `index-training-status.json` are in `fixtures/garmin/`; the
account id and device id, the latter also as the key of
`latestTrainingStatusData`, are replaced with placeholder 1.

Implemented. Each day posts one version 4 batch: version 3 plus
`trainingLoads` and `vo2maxEstimates`, either possibly empty. A day costs the
Activity list, sleep, the daily summary and training status, in that order.
A TrainingLoad is `weeklyTrainingLoad`, `loadTunnelMin` and `loadTunnelMax`
as Garmin sent them, with the payload archived, arrays removed. A VO2max
estimate is stored per sport and the date Garmin computed it, so the same
estimate read daily is one row. `get_sync_status` gained `TrainingLoad` and
`VO2max` collections timed by the last version 4 batch, and
`latestWellnessDay.holds` lists `TrainingLoad`.

Decided by the athlete during implementation: a training status Garmin dates
earlier than the day requested is not that day's, so the day is posted with no
TrainingLoad and completed. The earlier date is unobserved; the field is named
"latest", and stopping would wedge every nightly run at a day the watch missed.

Judgment calls, each open to correction: a training status or a VO2max
estimate dated after the day requested stops the run, since that would mean
the past-date reading broke. More than one device stops it too. VO2max is
Garmin's `vo2MaxPreciseValue` (39.3), not the rounded `vo2MaxValue`, as ADR
0012 quotes it, and its sports are Garmin's own keys, `generic` and
`cycling`, the only two ingest accepts. A re-sent estimate replaces its value.
Ingest takes each TrainingLoad figure as optional, 0 to 100000, and a VO2max
above 0 and at most 100, so an absent estimate is left out and never sent as
zero. A training status dated to the day but carrying none of the three
figures is no TrainingLoad, as a daily summary without wellness data is no
DailySummary; no such payload has been seen. The archive lives on the
TrainingLoad row, so a day without one archives nothing. Accepted risk, as on
ticket 05: no tool returns these figures, so the seams assert counts and
`holds` only.
