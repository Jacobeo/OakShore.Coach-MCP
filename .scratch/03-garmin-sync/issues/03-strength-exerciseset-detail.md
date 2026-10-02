# 03: Strength Activities with per-ExerciseSet detail

**What to build:** Every strength Activity the sync stores carries its
ExerciseSets — the Exercise performed, the repetitions and the weight lifted — so
questions about specific lifts over time can be answered. Cancelled sets are
stored as what they are and never counted as work.

**Blocked by:** 01 Activities into the store, safely.

**Status:** done

**Source:** [Garmin sync](../../03-garmin-sync.md), [ADR 0007](../../../docs/adr/0007-prescription-needs-per-exerciseset-detail.md).

- [x] Each strength Activity is enriched with one further call for its exercise
  sets; Cardio Activities are not enriched. An unanticipated sport is treated as
  Cardio rather than dropped, and the classification is asserted at the Python seam.
- [x] ExerciseSets persist with repetitions, weight and set type, attached to
  their Activity and carrying UserId.
- [x] Weight is normalised once, at the ingest boundary, to the canonical unit
  named in the contract. The grams the Garmin payload uses never reach the store.
- [x] A bodyweight working set is stored as a stated load, distinct from a set
  carrying no load at all.
- [x] A CancelledExerciseSet is stored and identifiable, and is excluded from
  volume and from per-set averages.
- [x] An Exercise category with no name is stored as a complete Exercise, not as
  missing data.
- [x] The spike 01 fixture corpus drives these cases at the Python seam, and the
  .NET seam asserts the stored shape through ingest.

## Comments

2026-10-02: Implemented. Judgment calls, each open to correction: the canonical
unit the contract (README's ingest rules) names is kilograms — `weightKg`, at
most four decimal places so ticket 10's FIT path converts exactly — because this
ticket's "grams never reach the store" overrides findings.md's grams-as-integer
suggestion and every contract field names its unit. Garmin's `-1` weight is
posted as `bodyweight: true` with `weightKg` omitted, keeping a stated
bodyweight load, a 0 kg set and an unrecorded load as three distinct answers.
Beyond the fields the ticket names, each set also carries startTimeUtc,
durationSeconds, wktStepIndex, candidateCount and topProbability (spec 03:
enrichment captures everything on the way in; the last two are findings.md's
Connect-edit detector). `get_sync_status` gained an ExerciseSet collection and
`latestActivityStrengthDetail`, where the CancelledExerciseSet exclusion from
volume and per-set averages is assertable; `get_activity` stays deferred.
Omitting `exerciseSets` from a later batch preserves stored sets, mirroring
`activityName`; an empty array clears them. Not captured here: the list entry's
`summarizedExerciseSets` rollup (ADR 0007 keeps it as a check and a fallback)
and `workoutId` provenance (findings.md's defensive list) — neither is named by
this ticket's criteria and no other ticket carries them, so they are flagged as
a follow-up rather than folded in.
