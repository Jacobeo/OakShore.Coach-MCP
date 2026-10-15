# Daily form is one training status call

Ticket 06 of spec 03 planned to store, per day, the training status payload's
acute load, chronic load, their ratio and its status, the monthly load-focus
buckets, VO2max, TrainingReadiness and the cycling FTP setting. A paced probe on
2026-10-08 (run `20261008T111226Z` of [`spikes/wellness-capture/`](../../spikes/wellness-capture/README.md),
four GETs for 2026-09-21) shows most of that does not exist for this athlete.

**What the watch does not produce.** The training status payload names the
device as a Forerunner 245.

- `/metrics-service/metrics/trainingreadiness/{date}` returned `[]`. There is no
  TrainingReadiness; Garmin derives it partly from HRV status, which this watch
  also lacks ([ADR 0011](0011-this-watch-records-no-hrv.md)).
- In `/metrics-service/metrics/trainingstatus/aggregated/{date}`,
  `acuteTrainingLoadDTO` is null: no acute load, chronic load, ratio or ratio
  status. `mostRecentTrainingLoadBalance.metricsTrainingLoadBalanceDTOMap` is
  null: no load-focus buckets.
- `/biometric-service/biometric/latestFunctionalThresholdPower/CYCLING` returned
  every field null: no FTP is set, and the athlete has no use for one.
- `/metrics-service/metrics/maxmet/daily/{date}/{date}` returned `[]`.

**What it does produce, all in the training status payload.** A
`weeklyTrainingLoad` (140) with its load tunnel (`loadTunnelMin` 67,
`loadTunnelMax` 218); `trainingStatus`, `loadLevelTrend` and `fitnessTrend`
codes, whose meanings Garmin does not publish; and `mostRecentVO2Max`, with a
generic estimate of 39.3 dated 2026-09-02 and a cycling estimate of 32.0 dated
2026-08-30. Asked for a past date, the payload describes that date: the response
for 2026-09-21, fetched on 2026-10-08, carries `calendarDate` 2026-09-21 and a
timestamp from that afternoon.

**So daily form is one call per day of data: the training status payload.**
TrainingReadiness, VO2max, load focus and FTP are not called. The weekly load
and its tunnel are the systemic axis of TrainingLoad
([ADR 0009](0009-trainingload-has-two-axes.md)). VO2max is read from the same
payload and stored per sport under the date Garmin computed it, so an unchanged
estimate re-read daily adds nothing. The codes are kept only in the archived
response, as [ADR 0010](0010-wellness-reads-sleep-and-the-daily-summary.md)
archives wellness. TrainingReadiness leaves the glossary: no concept, no term.

## Consequences

- A synced day costs four calls: the Activity list, sleep, the daily summary and
  training status, against the roughly six ADR 0005 planned.
- `get_current_form` carries no ACWR and no TrainingReadiness. Spec 04's
  readiness context and load-focus buckets have nothing to read; its "improve my
  VO2max" answer needs the intensity signal from somewhere else, such as the
  time in each HeartRateZone already stored per Cardio Activity, which spec 04
  decides.
- The past-date reading held over three consecutive dates in ticket 06's
  capture (run `20261015T070639Z`, indexed in
  [`fixtures/garmin/index-training-status.json`](../../fixtures/garmin/index-training-status.json)):
  each response carries the date requested and its own weekly load. A day the
  watch recorded nothing is unobserved; the sync treats a status Garmin dates
  earlier than the day requested as no status for that day.
- A new watch means re-running the capture spike and revisiting this decision.
