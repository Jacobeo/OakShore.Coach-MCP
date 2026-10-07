# 05: Daily wellness

**What to build:** For each day synced, the sync stores sleep, HRV, body battery,
stress and the daily summary, so recovery can be related to training. This is the
expensive half of the sync, at several calls per day, and it runs inside the
bounded and paced loop.

**Blocked by:** 02 Watermark, bounded and resumable runs.

**Status:** ready-for-agent

**Source:** [Garmin sync](../../03-garmin-sync.md).

- [ ] For each day within a run's bounds, sleep, HRV, body battery, stress and
  the daily summary are fetched and posted as part of that day's commit.
- [ ] Sleep is stored with start, end, duration and the stage minutes. Stage
  minutes are kept for later analysis and returned by no tool; duration reads as
  an upper bound on a fragmented night
  ([literature pass](../../literature-pass-2026-10-07.md)).
- [ ] Nightly HRV is stored with Garmin's weekly average, baseline band and
  status, never the nightly value alone.
- [ ] Body battery and stress are stored and returned by no tool.
- [ ] Wellness days obey the per-run ceiling and the most-recent-first ordering.
  A day is committed as a unit, so an interruption never leaves one half-stored.
- [ ] Pacing applies between per-day calls. The Python seam asserts the number
  and order of calls for a multi-day run.
- [ ] A day for which Garmin returns nothing is recorded as synced and empty,
  distinguishable from a day never attempted, so it is not retried forever.
- [ ] Stored values are readable through the counts-and-freshness tool at the
  .NET seam.
