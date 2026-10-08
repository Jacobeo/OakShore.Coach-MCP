# Wellness reads sleep and the daily summary, and archives both

[ADR 0005](0005-bounded-resumable-sync.md) budgeted roughly six per-date calls
for every day synced, body battery and stress among them. A paced capture on
2026-10-08 fetched the daily summary and the two dedicated endpoints for the
same day, 2026-09-21, so they could be compared byte for byte. The run's index
is [`fixtures/garmin/index-wellness.json`](../../fixtures/garmin/index-wellness.json)
and the capture script is [`spikes/wellness-capture/`](../../spikes/wellness-capture/).

**The daily summary already carries both.** `/usersummary-service/usersummary/daily`
holds 91 keys. Eight are body battery: `bodyBatteryChargedValue`,
`bodyBatteryDrainedValue`, `bodyBatteryHighestValue`, `bodyBatteryLowestValue`,
`bodyBatteryMostRecentValue`, `bodyBatteryDuringSleep`, `bodyBatteryAtWakeTime`
and `bodyBatteryVersion`. Eighteen are stress: `averageStressLevel`,
`maxStressLevel`, `stressQualifier`, the durations `stressDuration`,
`restStressDuration`, `activityStressDuration`, `uncategorizedStressDuration`,
`totalStressDuration`, `lowStressDuration`, `mediumStressDuration` and
`highStressDuration`, and a percentage beside each duration bar the total.

**The dedicated endpoints add only intraday series.**
`/wellness-service/wellness/bodyBattery/reports/daily` returns `charged` 55 and
`drained` 54, which are the summary's `bodyBatteryChargedValue` and
`bodyBatteryDrainedValue`, plus the day's window and `bodyBatteryValuesArray`.
`/wellness-service/wellness/dailyStress` returns `avgStressLevel` 22 and
`maxStressLevel` 92, which are the summary's, plus the window, two chart offsets,
and two 424-sample series. Spec 03 puts per-second metric series beyond zone
analysis out of scope, and these are exactly that.

**So a wellness day is two calls: sleep and the daily summary.** Both paths are
addressed by the athlete's display name, which the sync reads from the social
profile once per run. Heart-rate variability is not fetched either, for a
different reason ([ADR 0011](0011-this-watch-records-no-hrv.md)).

## What is kept

Typed columns hold what CONTEXT.md names and what a tool or the analytics in
build-order step 6 read: a SleepNight's start, end, duration and stage seconds;
the four BodyBattery figures; and StressLevel's average, maximum and the rest,
low, medium and high durations. They are authoritative.

Beside them, each response is archived verbatim with every array-valued member
removed — about 4 KB a day, 1.3 KB of sleep and 2.7 KB of summary. The asymmetry
decides it. A field not kept is recovered by re-walking history at three paced
calls a day, about twelve hours of calling for a decade against exactly the block
risk ADR 0005 exists to prevent, and nothing establishes that the bulk export
carries these daily fields at all. A typed column for each of the summary's 90
scalars would instead demand a glossary term for account ids and offsets, and
would still break when Garmin adds a field.

**The archive is read only by migration, backfill or analysis code promoting a
field into a column — never by an MCP tool and never at query time.** Without
that rule it becomes a second source of truth and [ADR 0002](0002-own-datastore-is-the-system-of-record.md)
stops meaning anything. Removing arrays is the scope boundary, and ingest
refuses an archived response that still holds one rather than stripping it, so
the boundary is held at both ends.

## Consequences

- ADR 0005's cost model narrows. A day synced costs the Activity list, sleep and
  the daily summary — three calls, against the roughly six it planned — before
  ticket 06 adds daily form.
- Garmin answers a day it holds nothing for with sleep of nulls and a summary
  whose `includesWellnessData` and `includesActivityData` are false (the
  2010-01-01 sentinel in the corpus). Neither becomes a row; the version 3 batch
  records the date as synced.
- The claim is falsifiable from the corpus: the three 2026-09-21 responses are
  committed. If a field turns out to live only on a dedicated endpoint, this
  decision changes rather than the field being dropped quietly.
