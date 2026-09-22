# 04: Findings note and fixture corpus

**What to build:** A person reads the dumps and the repo records the answer. The
spike exists to settle one question — do the athlete's on-watch weight and
exercise corrections survive into the Garmin API — and it is not done until that
question is answered in writing, for this athlete's own data, from observation.

The note states, field by field and citing the dump it came from:

- whether `exercises[].name` holds a real Exercise or `UNKNOWN`, and whether
  that differs between a followed Workout and a freestyle session
- whether `weight` is populated, and in which unit — this picks the canonical
  unit at the ingest boundary
- whether `setType` distinguishes working ExerciseSets from rest, so set counts
  are not inflated
- whether `repetitionCount` matches what the athlete actually lifted
- how many CancelledExerciseSets the corpus holds, and what they cost the
  per-set averages they are excluded from
- whether `summarizedExerciseSets` is populated in practice, so per-Exercise
  aggregates can be trusted or discarded
- whether the Activity list carries `totalSets`, `activeSets` and `totalReps`
- the actual shape of `hrTimeInZones`, and the zone boundaries per sport profile

One thing not to rediscover as corrupt data: an `ACTIVE` ExerciseSet carrying a
weight, a few seconds of duration and `repetitionCount` 0 is a
CancelledExerciseSet, and the athlete confirmed the mechanism — skipping or
deferring a step of a Workout on the watch means starting that Exercise and
cancelling it, which writes exactly one zero-repetition set. It is Adherence
evidence, joinable to the prescribed step through `wktStepIndex`, so the note
should count these rather than filter them. `activeSets` in the Activity rollup
counts them too, which is one more thing the rollup cannot tell apart from a
completed set.

It then names which project the answer implies, because the two outcomes lead to
different products. If weight and Exercise names are present, the strength
analytics are viable immediately, the bulk export becomes valuable for
backfilling years of it, and Garmin write-back (spec 05) stays a convenience. If
they are absent, write-back is promoted from convenience to the mechanism that
makes the data exist at all — the watch only records Exercise names when
following a downloaded Workout — and spec 05 moves much earlier in the build
order. Say which one happened, and whether `docs/build-order.md` needs to change.

The dumps themselves land in a stable, indexed location and become the durable
output of this spike: the replay fixtures for the Python seam, and the source of
the batch payloads that seed the .NET seam through the ingest endpoint. Before
they are committed, decide what in a real Garmin payload should not go into the
repo — account and profile identifiers, device identifiers, anything locating a
session — and record the decision either way, including "nothing needed
scrubbing".

No automated tests. Writing tests for throwaway code that exists to answer a
question would be ceremony; success is the question being answerable from the
dumps.

**Blocked by:** 02 (ExerciseSet dumps for strength Activities), 03 (Heart-rate
zone dumps for cardio Activities)

**Status:** done

- [x] A findings note in the repo states, for this athlete's own data, whether
      `weight` and Exercise names are present
- [x] Each of the field questions above is answered from a named dump file, not
      from documentation
- [x] The unit of `weight` is stated, or its absence is stated
- [x] The note names which of the two project forks applies, and whether
      `docs/build-order.md` changes as a result
- [x] The dumps live in a stable location with an index saying which endpoint and
      which Activity each file came from
- [x] A decision is recorded on what was scrubbed from the committed payloads,
      or that nothing needed scrubbing
- [x] No follow-up question in this spec requires re-fetching anything
