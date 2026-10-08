# This athlete's watch records no HRV, so the sync does not ask for it

Ticket 05 of spec 03 planned to store nightly heart-rate variability with
Garmin's weekly average, baseline band and status. The paced capture of
2026-10-08 (run `20261008T103754Z`, indexed in
[`fixtures/garmin/index-wellness.json`](../../fixtures/garmin/index-wellness.json))
shows there is nothing to store.

**The evidence.** `/hrv-service/hrv/{date}` answered 204 with a zero-byte body
for 2026-09-19, 2026-09-20 and 2026-09-21, and for the 2010-01-01 sentinel. The
sleep response's `dailySleepDTO` carries 29 keys and none of them is
`avgOvernightHrv`, `hrvStatus` or `sleepScores` — absent, not null — where
Garmin includes them for watches that measure overnight HRV.

This is a specific capability gap, not a watch that reports nothing: the same
payloads have `deviceRemCapable` true and every sleep stage in seconds. The
training status payload names the device as a Forerunner 245.

**So the sync makes no HRV call, and HRV is dropped from ticket 05.** A call
that answers 204 every day is pure cost against
[ADR 0005](0005-bounded-resumable-sync.md)'s budget. No storage, ingest field or
glossary term exists for it: no concept, no term.

## Consequences

- The literature pass's HRV protocol — a rolling 7-day lnRMSSD against the
  baseline band — cannot be implemented for this athlete, and is marked so in
  [the literature pass](../../.scratch/literature-pass-2026-10-07.md).
- The [metric backtest spike](../../.scratch/metric-backtest-spike.md)'s question
  about the HRV weekly average leaving its band is blocked on hardware.
- Garmin's TrainingReadiness takes HRV status as an input, and a probe of the
  same day returned no TrainingReadiness at all; ticket 06 records that before it
  is planned.
- A new watch means re-running the capture spike and revisiting this decision.
