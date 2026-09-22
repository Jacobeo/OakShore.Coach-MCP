# Prescription reads per-ExerciseSet rows, not the Activity rollup

The Activity list turns out to carry `summarizedExerciseSets`: a per-Exercise
rollup with a real category and name, a set count, total repetitions, total
volume and a max weight. It is populated for this athlete, and it arrives free
with a call the sync makes anyway. That makes it tempting to treat as the
strength model and skip the per-Activity `exerciseSets` call entirely.

It is not enough, because the rollup is lossy in exactly the dimension a
prescription depends on.

**Set-to-set decay is invisible.** Three sets of bench at 70 kg recorded as
8, 8, 8 and as 10, 8, 6 produce an identical rollup: 3 sets, 24 repetitions,
1680 kg of volume, 70 kg max. The first says the load was comfortable and
should go up; the second says the athlete was at their limit and it should
hold. Opposite prescriptions, indistinguishable inputs.

**Within-Exercise weight variation is flattened.** Across 42 per-Exercise
rollups in a seven-week window, 4 carry a volume that is not repetitions times
max weight, so the weight demonstrably moved between sets and the rollup
records only the peak. Two of the four are the trap bar deadlift — the main
lift, on both the weeks it appears. Its 2026-09-07 entry, 3 sets and 9
repetitions at a 110 kg max for 930 kg of volume, fits a 100/100/110 ramp and
equally fits a 90 kg warm-up triple before two working triples at 110. The
working weight is 105 or it is 110, and the rollup cannot say which.

**Rest is unrepresented.** `totalSets` equals `activeSets` on all seven
strength Activities in that window, so the rollup carries no signal about rest
sets at all. `setType` on the per-set rows is the only thing that distinguishes
working sets from rest, and without it set counts are open to inflation.

Prescribing from previous prescriptions instead is not an alternative. The
athlete corrects repetitions and weight on the watch mid-session, which is
precisely the divergence between planned and actual that Adherence exists to
measure and that [ADR 0002](0002-own-datastore-is-the-system-of-record.md)
makes the datastore the record of.

**So the sync fetches `exerciseSets` for every strength Activity and the store
keeps per-set rows.** The rollup is kept alongside them, not discarded: it is
an independent check on the per-set totals, and a degraded fallback for any
Activity whose per-set rows turn out to be empty.

## Consequences

- No change to the cost model or the build order. One enrichment call per
  strength Activity was already budgeted in
  [ADR 0005](0005-bounded-resumable-sync.md) and listed in step 3 of
  [the build order](../build-order.md).
- The per-Activity *summary* endpoint stays unused, which is what spec 01
  meant by the list already carrying the totals. That call is the one being
  skipped; `exerciseSets` is not.
- `ExerciseSet` keeps its own weight and repetition count rather than being
  derived from a per-Exercise aggregate, and the aggregate is stored as an
  observation rather than as the source of truth.
- Analytics that only need volume or a per-Exercise trend can read the rollup
  and stay correct if the per-set rows are ever missing.
- This assumes per-set `weight` and `repetitionCount` are populated for
  watch-recorded strength. That is unverified — it is the question spec 01's
  second issue answers. If they come back null, the rollup becomes the only
  strength data the API offers, and Garmin write-back stops being a
  convenience: the watch only records Exercise names when following a
  downloaded Workout, so spec 05 would move much earlier.
