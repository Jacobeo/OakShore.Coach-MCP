# FIT cannot state a bodyweight load, and the store says so

An ExerciseSet carries three distinct answers about load, which
[CONTEXT.md](../../CONTEXT.md) and the ingest contract never collapse: a stated
bodyweight load, a set performed with no external load, and no load recorded at
all. Garmin's API distinguishes them with a `-1` grams sentinel for bodyweight.

**FIT has no such sentinel.** Decoding the athlete's own export shows Garmin
writing the same three bodyweight sets of one Activity (23898645459, standing
calf raises) two different ways: one with no `weight` field at all, two with
`weight` of zero. The API reports `-1` for all three. FIT's two encodings mean
"no load recorded" and "no external load" in the contract, and neither of them
means bodyweight.

**The FIT path reports what FIT holds and never claims a bodyweight load.** A set
read from FIT carries `weightKg: 0` where FIT states zero and no weight at all
where FIT states none. Inventing `bodyweight: true` from either would be a guess
recorded as a fact.

**So every stored ExerciseSet carries its ExerciseSetSource**, and a reader that
cares about bodyweight work knows which rows can answer and which cannot. Where
an Activity exists in both sources the API's reading wins, by ADR 0014's rule
that a Backfill leaves a stored Activity alone, so only history the API was never
asked for reads this way.

`candidateCount` and `topProbability` divide the same way. FIT's `category` field
is three classification slots, the same three the API lists as `exercises`, so
the count of slots a file fills is `candidateCount`; FIT carries no probability,
so `topProbability` is absent rather than invented.

## Consequences

- Volume over backfilled history understates bodyweight work, exactly as it does
  for an API-derived bodyweight set, whose load is also not stated in kilograms.
- A set FIT wrote with a zero weight and one performed with no external load are
  indistinguishable in backfilled history. The source field is what says so.
- Re-syncing a historic Activity through the API with `--from-date` and
  `--to-date` replaces its FIT-derived sets with the API's, bodyweight included.
