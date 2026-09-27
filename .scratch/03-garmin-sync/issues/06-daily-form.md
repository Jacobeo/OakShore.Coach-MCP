# 06: Daily form

**What to build:** For each day synced, the sync also stores training status,
TrainingLoad, TrainingReadiness and VO2max, so the athlete's current form can be
assessed alongside what they did.

**Blocked by:** 05 Daily wellness.

**Status:** ready-for-agent

**Source:** [Garmin sync](../../03-garmin-sync.md).

- [ ] Training status, TrainingLoad, TrainingReadiness and VO2max are fetched
  within the same per-day loop and committed with that day.
- [ ] TrainingLoad is stored with both its short and its long window, so their
  ratio is derivable rather than stored as a third number that can disagree.
- [ ] The added calls stay inside the per-run ceiling; the Python seam asserts
  that a run's total call count still respects it.
- [ ] A day missing one of these metrics stores the rest rather than failing the
  day, and the absence is distinguishable from a zero.
- [ ] Stored values are readable through the counts-and-freshness tool at the
  .NET seam.
