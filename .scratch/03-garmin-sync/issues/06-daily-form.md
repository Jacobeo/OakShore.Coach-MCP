# 06: Daily form

**What to build:** For each day synced, the sync also stores the training status
payload — TrainingLoad, load focus and VO2max — plus TrainingReadiness and the
cycling FTP setting, so the athlete's current form can be assessed alongside
what they did.

**Blocked by:** 05 Daily wellness.

**Status:** ready-for-agent

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
