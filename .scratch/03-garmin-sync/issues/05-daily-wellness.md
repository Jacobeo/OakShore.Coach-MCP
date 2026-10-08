# 05: Daily wellness

**What to build:** For each day synced, the sync stores sleep, body battery,
stress and the daily summary, so recovery can be related to training. This is the
expensive half of the sync, at two calls per day, and it runs inside the bounded
and paced loop.

**Blocked by:** 02 Watermark, bounded and resumable runs.

**Status:** done

**Source:** [Garmin sync](../../03-garmin-sync.md).

- [x] For each day within a run's bounds, sleep and the daily summary are fetched
  and posted as part of that day's commit. BodyBattery and StressLevel are read
  off the DailySummary; the dedicated body battery and stress endpoints and the
  HRV endpoint are not called
  ([ADR 0010](../../../docs/adr/0010-wellness-reads-sleep-and-the-daily-summary.md),
  [ADR 0011](../../../docs/adr/0011-this-watch-records-no-hrv.md)).
- [x] Sleep is stored with start, end, duration and the stage minutes. Stage
  minutes are kept for later analysis and returned by no tool; duration reads as
  an upper bound on a fragmented night
  ([literature pass](../../literature-pass-2026-10-07.md)).
- [x] Body battery and stress are stored and returned by no tool.
- [x] Wellness days obey the per-run ceiling and the most-recent-first ordering.
  A day is committed as a unit, so an interruption never leaves one half-stored.
- [x] Pacing applies between per-day calls. The Python seam asserts the number
  and order of calls for a multi-day run.
- [x] A day for which Garmin returns nothing is recorded as synced and empty,
  distinguishable from a day never attempted, so it is not retried forever. It
  counts towards the per-run ceiling, the Watermark completes it, and
  `get_sync_status` reports it as the latest day reached, holding nothing.
- [x] Stored values are readable through the counts-and-freshness tool at the
  .NET seam.

## Comments

2026-10-08: The fixture corpus held no wellness responses, so one paced,
read-only capture ran from the home machine (`spikes/wellness-capture/`): the
social profile, then sleep, HRV and the daily summary for 2026-09-19 to
2026-09-21 and a 2010-01-01 sentinel, then the dedicated body battery and stress
endpoints for 2026-09-21. Fifteen GETs, no refusal. The scrubbed responses are in
`fixtures/garmin/`. Two findings changed the ticket, each decided by the athlete:
the HRV endpoint answered 204 on every date and the watch records no overnight
HRV, so the HRV criterion is removed rather than rewritten (ADR 0011); and the
daily summary carries every body battery and stress figure the dedicated
endpoints do, which add only intraday series, so a wellness day is two calls
(ADR 0010). A day synced is three calls with the Activity list, against the five
this ticket assumed.

Implemented. Each Watermark day posts one version 3 batch: its Activities, its
SleepNight and its DailySummary, either list possibly empty. Version 3 states
every date in its range was synced, which is how an empty day differs from one
never attempted. Typed columns hold what CONTEXT.md names; each response is also
archived with its arrays removed, read by no tool (ADR 0010). `get_sync_status`
gained SleepNight and DailySummary collections and `latestWellnessDay`, the latest
date a version 3 batch reached and which kinds of record it holds — no values.

Judgment calls, each open to correction: `holds` lists BodyBattery and StressLevel when the
DailySummary carries any of their figures, so "everything" and "sleep only" read
apart. A re-sent day replaces its figures; a later batch that leaves a day's
SleepNight out keeps the stored one, as Activities are kept, and an omitted
`garminResponse` keeps the archived one. The SleepNight's duration is Garmin's
`sleepTimeSeconds`, which excludes awake time; stage times include the
unmeasurable bucket. A privacy-protected summary stops the run rather than
completing the day empty, and so does a response dated for a different day than
the one requested, rather than storing it under the wrong date. The display name both paths need costs one social
profile call per run.

The capture's probe of ticket 06's endpoints is recorded on that ticket.

2026-10-08, after review: the athlete chose whole minutes for every SleepNight
duration; the sync converts Garmin's seconds before posting, as it converts
grams to kilograms. StressLevel durations stay in seconds. Accepted risk, for
now: no tool returns the stored SleepNight minutes or the BodyBattery and
StressLevel figures, so neither seam asserts those columns beyond `holds`, and
a column written wrongly would pass every test until a tool or analysis reads
them.

The athlete chose that an explicit `--from-date`/`--to-date` run gets the same
per-day loop: each day its own version 3 batch with SleepNight and DailySummary,
most recent first, bounded by `--max-days`, recorded in the Watermark, and
refusing a `--to-date` after today, which would otherwise be completed before it
happened. Days the Watermark already completed are synced again, because they
were asked for. Making the Watermark write many times in quick succession
exposed Windows briefly refusing to replace a file just written; the Watermark
now retries that replace a few times before failing.
