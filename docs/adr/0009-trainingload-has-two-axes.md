# TrainingLoad has two axes

Garmin computes training load from heart rate (EPOC), so a strength Activity
barely moves it: a hard strength week reads as rest. For an athlete whose
training is half strength work, the single number misstates the thing it exists
to say — how much training cost has accumulated.

**TrainingLoad therefore carries two axes, and the server computes only one of
them.**

- **Systemic axis.** Garmin's weekly training load — the last seven days of
  EPOC, the recovery cost Garmin estimates from heart rate — against its load
  tunnel, the range Garmin's own model judges optimal for the athlete's recent
  fitness and training. Both are stored verbatim from the daily training status
  payload. Nothing is recomputed and no third number is derived. This athlete's
  watch sends no acute load, chronic load or ratio
  ([ADR 0012](0012-daily-form-is-one-training-status-call.md)), so none is
  stored and none is computed in its place.
- **Strength axis.** The count of working ExerciseSets: the last 7 days (acute)
  against the 4-week weekly average (chronic), computed from stored ExerciseSets
  at query time. No ratio and no status — a ratio of small integers (a normal
  week is 12–20 working sets) is noise, and no validated threshold exists to
  classify it against.

## Considered and rejected

- **Recomputing the cardio side (zone-based TRIMP).** A second unvalidated model
  of something Garmin already measures, with more data than the store holds. If
  the backfilled history ever shows Garmin's figures to be wrong for this
  athlete, that is the moment to revisit — see the
  [backtest spike](../../.scratch/metric-backtest-spike.md).
- **Summed kilograms (volume-load) for the strength axis.** It conflates work
  done with strength and with fatigue, it is distorted by Exercise selection — a
  day of isolation work and a day of compound lifts can share a total — and it
  needs an arbitrary valuation for bodyweight ExerciseSets. A working-set count
  has none of these problems, and a bodyweight set counts like any other.

## The tunnel describes load progression, never a target

Garmin does not publish how the tunnel is computed, and no independent study
validates it. The nearest studied idea, the acute:chronic ratio's 0.8–1.3
"sweet spot", turned out to be a statistical artifact of binning: treated
continuously, the association with injury disappears (Impellizzeri et al. 2020,
IJSPP and Sports Med), and later responses from the framework's originators
concede it does not predict injury. The tunnel earns the same restraint. Its use
is descriptive: weekly load above the tunnel is a prompt to ask about fatigue
and to avoid stacking another hard week, nothing more. Every surface — tool
wording and response fields — presents the load against its tunnel as load
progression, never as a target and never as injury risk. At three sessions a
week the load behind it is noisy, which argues for the same restraint.

## Consequences

- Wherever a trusted range exists, a form field returns a triple — status, value
  and the range — so the agent can classify and explain. The systemic axis is
  that triple: the weekly load, the tunnel, and whether the load sits below,
  inside or above it. Where no range exists (the strength axis), the shape is
  value plus average, with no status to fake.
- The strength axis adds no sync calls; it derives from ExerciseSets already
  stored.
- Ticket 06 of the sync stores the weekly load and the tunnel's bounds verbatim,
  from one training status call per day.
- CONTEXT.md's TrainingLoad names both axes; code and tests follow it.
- These thresholds are group-level findings applied to one athlete. The backtest
  spike against the backfilled history personalises or confirms them; until
  then they act as conservative flags. Supporting citations are recorded in
  [the literature pass](../../.scratch/literature-pass-2026-10-07.md).
