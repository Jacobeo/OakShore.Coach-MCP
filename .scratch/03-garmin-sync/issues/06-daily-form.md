# 06: Daily form

**What to build:** For each day synced, the sync also stores the training status
payload — TrainingLoad, load focus and VO2max — plus TrainingReadiness and the
cycling FTP setting, so the athlete's current form can be assessed alongside
what they did.

**Blocked by:** 05 Daily wellness.

**Status:** needs-triage

**Source:** [Garmin sync](../../03-garmin-sync.md),
[ADR 0009](../../../docs/adr/0009-trainingload-has-two-axes.md).

- [ ] Training status, TrainingLoad, TrainingReadiness and VO2max are fetched
  within the same per-day loop and committed with that day.
- [ ] TrainingLoad's systemic axis is stored verbatim from the payload — acute,
  chronic, ratio, ratio status and the optimal chronic band — per ADR 0009.
  Nothing is recomputed; the strength axis derives at query time from stored
  ExerciseSets and needs no sync work.
- [ ] The monthly load-focus buckets — low aerobic, high aerobic, anaerobic —
  are stored with their target bands.
- [ ] VO2max is stored for generic and cycling, and the cycling FTP setting with
  it. Running lactate threshold is not fetched: the athlete's watch cannot
  produce one.
- [ ] The added calls stay inside the per-run ceiling; the Python seam asserts
  that a run's total call count still respects it.
- [ ] A day missing one of these metrics stores the rest rather than failing the
  day, and the absence is distinguishable from a zero.
- [ ] Stored values are readable through the counts-and-freshness tool at the
  .NET seam.

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
