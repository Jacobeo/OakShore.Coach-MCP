# 03: Strength Activities with per-ExerciseSet detail

**What to build:** Every strength Activity the sync stores carries its
ExerciseSets — the Exercise performed, the repetitions and the weight lifted — so
questions about specific lifts over time can be answered. Cancelled sets are
stored as what they are and never counted as work.

**Blocked by:** 01 Activities into the store, safely.

**Status:** ready-for-agent

**Source:** [Garmin sync](../../03-garmin-sync.md), [ADR 0007](../../../docs/adr/0007-prescription-needs-per-exerciseset-detail.md).

- [ ] Each strength Activity is enriched with one further call for its exercise
  sets; Cardio Activities are not enriched. An unanticipated sport is treated as
  Cardio rather than dropped, and the classification is asserted at the Python seam.
- [ ] ExerciseSets persist with repetitions, weight and set type, attached to
  their Activity and carrying UserId.
- [ ] Weight is normalised once, at the ingest boundary, to the canonical unit
  named in the contract. The grams the Garmin payload uses never reach the store.
- [ ] A bodyweight working set is stored as a stated load, distinct from a set
  carrying no load at all.
- [ ] A CancelledExerciseSet is stored and identifiable, and is excluded from
  volume and from per-set averages.
- [ ] An Exercise category with no name is stored as a complete Exercise, not as
  missing data.
- [ ] The spike 01 fixture corpus drives these cases at the Python seam, and the
  .NET seam asserts the stored shape through ingest.
