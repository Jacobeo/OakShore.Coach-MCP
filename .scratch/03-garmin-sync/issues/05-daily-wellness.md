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
- [ ] Wellness days obey the per-run ceiling and the most-recent-first ordering.
  A day is committed as a unit, so an interruption never leaves one half-stored.
- [ ] Pacing applies between per-day calls. The Python seam asserts the number
  and order of calls for a multi-day run.
- [ ] A day for which Garmin returns nothing is recorded as synced and empty,
  distinguishable from a day never attempted, so it is not retried forever.
- [ ] Stored values are readable through the counts-and-freshness tool at the
  .NET seam.
