# Findings — strength data spike

The answer to the question spec 01 exists to settle, read off this athlete's own
data. Every claim below cites the dump it came from. Nothing here is taken from
Garmin's documentation or from the research that preceded the spike.

The corpus is the run of 2026-09-22, a 48-day window from 2026-08-05: 15
Activities, 7 of them strength Activities, 8 cardio. It is committed, scrubbed,
under [`fixtures/garmin/`](../../fixtures/garmin/) with its own
[README](../../fixtures/garmin/README.md) and the `index.json` the harness wrote.
File names below are relative to that directory. Every figure was recomputed
against the committed corpus rather than against the raw dumps, so the numbers
in this note are reproducible from what is in the repository.

## The answer

**`weight` is populated. Exercise identity is populated. The athlete's on-watch
corrections survive into the Garmin API.**

The proof turns on how a Garmin Workout is built, which the athlete states
plainly: **a Workout step names one Exercise with one target — reps and weight —
and a repeat count.** Every ExerciseSet of a step therefore shares one target by
construction. The prescription cannot produce two different numbers under one
`wktStepIndex`. So **any within-step variation is a correction**, and the only
places a correction can come from are the watch during the Activity and Garmin
Connect afterwards.

That turns a per-set field into a falsifiable test, and the corpus answers it.
`activity-service--exerciseSets__activity-24185357395.json`, `wktStepIndex` 5 —
one step, one trap bar deadlift target, three sets:

| reps | weight |
| --- | --- |
| 3 | 95 000 g |
| 3 | 100 000 g |
| 3 | 105 000 g |

Three weights under one target is a correction, and the athlete confirms this one
was made on the watch. Across the corpus, **5 of the 34 recorded steps carry
within-step variation** (cancelled sets excluded, since those vary by
definition):

| Activity | step | sets as recorded | corrected |
| --- | --- | --- | --- |
| 24185357395 | 5 | 3×95, 3×100, 3×105 kg | weight |
| 24185357395 | 8 | 5×10, 5×12 kg | weight |
| 24272601315 | 4 | 3×100, 3×100, 3×110 kg | weight |
| 24444892080 | 4 | 8, 4, 4 at 75 kg | reps |
| 23898645459 | 15 | 5, 10, 10 at bodyweight | reps |

Both fields move, independently, in both directions. **The corrections are in the
API, per set.**

What they are *not* is a measurement against Garmin's prescription, and it is
worth being exact about that here rather than leaving it implied.
`wktStepIndex` groups the sets of a block; it does not carry the block's target,
and the `workoutId` that would is not something the store can rely on resolving —
see [What `wktStepIndex` is for, and what it is
not](#what-wktstepindex-is-for-and-what-it-is-not). So the value of these five
rows is intrinsic: a descending rep ladder at a fixed weight says the athlete was
at their limit, and an ascending weight ladder at fixed reps says they were
warming up into a working set. Those readings need no plan at all, and they are
exactly the set-to-set detail
[ADR 0007](../../docs/adr/0007-prescription-needs-per-exerciseset-detail.md)
says a prescription depends on.

So **fork 1 applies** (see [Which project this implies](#which-project-this-implies)).

## Field by field

### `exercises[].name` — populated, but it is not the identifier

`exercises[]` is not one Exercise. It is a **ranked candidate list**: Garmin's
classification of the movement, each entry carrying a `probability`. Across the
seven dumps, 92 of the 107 ACTIVE ExerciseSets carry three candidates and 15
carry one, for 291 candidate entries in total. REST rows carry none.

Reading the top candidate of every one of the 107 ACTIVE rows yields 11 distinct
Exercises. Ten are real movements. One is not:

| Exercise (category, name) | ACTIVE rows |
| --- | --- |
| `BENCH_PRESS`, name `null` | 22 |
| `ROW` / `SEATED_CABLE_ROW` | 22 |
| `DEADLIFT` / `TRAP_BAR_DEADLIFT` | 14 |
| `CHOP` / `CABLE_ROTATIONAL_LIFT` | 13 |
| `BANDED_EXERCISES` / `GLUTE_BRIDGE` | 11 |
| `LEG_CURL` / `WEIGHTED_LEG_CURL` | 7 |
| `LUNGE` / `DUMBBELL_BULGARIAN_SPLIT_SQUAT` | 6 |
| `SQUAT` / `DUMBBELL_SPLIT_SQUAT` | 6 |
| `CALF_RAISE` / `STANDING_CALF_RAISE` | 3 |
| `CORE` / `CABLE_CORE_PRESS` | 2 |
| `UNKNOWN`, name `null` | 1 |

Two things to take from that table.

**`category` is always populated; `name` is optional.** 63 of the 291 candidate
entries in the corpus carry `name: null`, and 61 of those 63 are `BENCH_PRESS` —
the flat barbell bench press, which is the category with no sub-variant to name.
The other two are the single `UNKNOWN` row below. So the Exercise is the pair,
with the name as an optional refinement, and a null name is a complete answer
rather than missing data. Code that reads `name` as the Exercise loses the
athlete's most frequent lift.

**`UNKNOWN` appears exactly once in 107 rows,** and only as the top candidate of
a row whose three candidates disagree — the only such row in the corpus:

```
activity-service--exerciseSets__activity-24185357395.json, messageIndex 34
  [ {UNKNOWN, null, 99.609375}, {CURL, null, 0.0}, {BENCH_PRESS, null, 0.0} ]
  setType ACTIVE, repetitionCount 0, weight null, duration 1.973s, wktStepIndex null
```

That is a CancelledExerciseSet the watch could not classify at all, not a
degraded recording of a real set. The degraded case the research warned about —
accelerometer-inferred sets with `weight` null and the category `UNKNOWN`
throughout — **does not occur in this history**. Every strength Activity in the
window followed a Workout pushed to the watch
(`workoutId` non-null on all 7 list entries: 1675565879 on the four most recent,
1535387640 on the three older). There is no freestyle Activity in the corpus, so
the difference between the two cases could not be observed and nothing should be
designed for the case that was not seen.

### `weight` — populated, in **grams**

`biometric-service--heartRateZones.json` aside, the unit is settled by the values
themselves. The 16 distinct positive weights in the corpus:

```
10000  12000  12500  13500  35000  55000  67500  70000
75000  80000  85000  95000  100000  105000  110000  115000
```

Read as grams these are 10 to 115 kg, with the trap bar deadlift topping out at
115 kg and the dumbbells at 12–13.5 kg — the right magnitudes for this athlete's
lifts, and no other scale fits both ends. **Every one is a multiple of 500 g**
and only some are multiples of 1 kg (67 500, 12 500, 13 500), which is the plate
and dumbbell increment showing through. That is further evidence the numbers were
entered rather than inferred: an accelerometer estimate would not land on 67.5 kg.

**So grams is the unit Garmin sends, and the canonical unit at the ingest
boundary.** Storing grams as an integer keeps every observed value exact and
makes 0.5 kg representable without a float.

**`weight` is never absent on a set that was actually performed.** All 99 working
ACTIVE rows carry a value. The only two nulls among ACTIVE rows are
CancelledExerciseSets, where there is nothing to record.

The three non-positive values, and what each one means:

| value | rows | what it is |
| --- | --- | --- |
| `null` | 94 | 92 REST rows, plus 2 cancelled ACTIVE rows |
| `-1.0` | 18 | 3 working `STANDING_CALF_RAISE` rows, plus 15 REST rows in one Activity |
| `0.0` | 12 | working, banded movements with no external load |

**`-1.0` means bodyweight.** Not "unknown", and not "zero" — a stated load whose
magnitude is the athlete's own mass. Garmin Connect renders those same three sets
as `Bodyweight` in both the weight and the volume column, and the athlete
confirms that is what they were: 5, 10 and 10 standing calf raises at
`wktStepIndex` 15 of
`activity-service--exerciseSets__activity-23898645459.json`.

So the three non-positive values are three different answers and must not be
collapsed: `null` is nothing performed, `-1` is bodyweight, `0` is performed with
no external load. Only `0` and `-1` appear on working sets, and they mean
different things — a banded glute bridge at `0` genuinely adds no load beyond
the band, while a calf raise at `-1` is loaded by the athlete.

**Garmin's own rollup loses that distinction.** The `CALF_RAISE` entry in that
Activity's `summarizedExerciseSets` reports `maxWeight` 0 and `volume` 0 against
25 repetitions — 25 loaded repetitions recorded as no work at all. That is one
more dimension in which the rollup is lossy and the per-set rows are not, on top
of the set-to-set decay and weight variation
[ADR 0007](../../docs/adr/0007-prescription-needs-per-exerciseset-detail.md)
already records.

It also leaves a question this spike does not answer: whether *our* volume for a
bodyweight set should stay zero, as Garmin has it, or be computed from the body
weight on the AthleteProfile. Both are defensible and the choice belongs with the
analytics in step 6, not here. What matters at the ingest boundary is that the
bodyweight case arrives distinguishable, which it does.

### `setType` — yes, it separates working from rest

Exactly two values across the corpus, `ACTIVE` and `REST`, 107 of each. Every
REST row carries an empty `exercises[]` array, and 92 of the 107 carry
`repetitionCount: null`. So rest is unambiguous and set counts are not at risk of
inflation from it.

The counts are symmetric by coincidence of this athlete's pattern, not by rule:
`activity-service--exerciseSets__activity-24185357395.json` holds 17 ACTIVE rows
and 18 REST. Nothing should assume they pair up.

### `repetitionCount` — matches what was lifted

It varies set to set within a single Workout step, which is the same evidence
that settled the weight question. `activity-service--exerciseSets__activity-24444892080.json`,
`wktStepIndex` 4: 8, 4, 4, 0 repetitions. A prescribed step does not produce a
descending ladder; an athlete at their limit does.

Summed over the ACTIVE rows of each dump it reproduces `totalReps` from the list
entry exactly, on all seven Activities (83, 71, 70, 88, 81, 83, 82). The two
sources agree, so neither is guessing.

### CancelledExerciseSets — 8 in the corpus, and duration cannot find them

Eight of the 107 ACTIVE rows carry `repetitionCount: 0`:

| Activity | Exercise | `wktStepIndex` | duration | `weight` |
| --- | --- | --- | --- | --- |
| 24011353395 | `LEG_CURL` / `WEIGHTED_LEG_CURL` | 12 | 3.179 s | 55 000 g |
| 24098326007 | `ROW` / `SEATED_CABLE_ROW` | 10 | 3.843 s | 80 000 g |
| 24098326007 | `CHOP` / `CABLE_ROTATIONAL_LIFT` | 16 | 3.697 s | 12 000 g |
| 24185357395 | `BENCH_PRESS` | 11 | 3.242 s | 75 000 g |
| 24185357395 | `UNKNOWN` | `null` | 1.973 s | `null` |
| 24272601315 | `BANDED_EXERCISES` / `GLUTE_BRIDGE` | 0 | 6.973 s | `null` |
| 24444892080 | `ROW` / `SEATED_CABLE_ROW` | 7 | 4.411 s | 85 000 g |
| 24444892080 | `BENCH_PRESS` | 4 | 12.544 s | 75 000 g |

**Most of them are not skipped work.** Checking each against the other rows of
its own Activity — did any working set land on the same `wktStepIndex`? — splits
the eight three ways:

| | count |
| --- | --- |
| **Resumed** — the block was completed later in the same session | 5 |
| **Abandoned** — no working set at that step anywhere in the Activity | 2 |
| No `wktStepIndex`, so unknowable | 1 |

So a CancelledExerciseSet marks a block *started and cancelled*, and nothing
more. Whether that was a skip or a deferral is a separate question, answered by
the rest of the Activity rather than by the row itself, and in this corpus the
answer is usually "deferral". Reading all eight as skipped work would report this
athlete as having missed five pieces of training they actually did.

This is also the strongest practical argument for storing `wktStepIndex`: without
it a skip and a deferral are indistinguishable. The eighth row, the unclassifiable
`UNKNOWN`, has no step and evidences nothing beyond its own existence.

**`repetitionCount == 0` is the only reliable signal.** The duration heuristic in
the issue description does not survive the corpus: cancelled sets run from 1.973 s
to **12.544 s**, while working sets run from **0.642 s** to 265.674 s. The ranges
overlap, so any duration threshold both misses cancelled sets and discards real
ones.

What they cost the averages they are excluded from, over the whole corpus:

| | all 107 ACTIVE rows | 99 working rows | error if not excluded |
| --- | --- | --- | --- |
| mean repetitions per set | 5.21 | 5.64 | understated 7.6 % |
| mean duration per set | 54.7 s | 58.7 s | understated 6.8 % |
| total volume | 25 664 kg | 25 664 kg | none |

Volume is unaffected — zero repetitions contribute zero — so the damage is
confined to per-set averages, and at 8 rows in 107 it is a 7 % error on the two
figures a prescription reads most directly.

### `summarizedExerciseSets` — populated, and exactly reproducible

Present and non-empty on all 7 strength list entries, absent on all 8 cardio
entries: 42 per-Exercise rollups in total. Each carries `category`, an optional
`subCategory`, `sets`, `reps`, `volume`, `duration` and `maxWeight`.

It can be trusted, in the narrow sense that it is arithmetically consistent with
the per-set rows. All 42 entries reproduce, to the byte, from the ACTIVE rows of
the matching `exerciseSets` dump under these rules:

- `sets` = count of ACTIVE rows, CancelledExerciseSets included
- `reps` = Σ `repetitionCount`
- `maxWeight` = max(`weight`, 0) — so a bodyweight set's `-1` floors to 0
- `volume` = Σ `repetitionCount` × max(`weight`, 0), in grams
- `duration` = Σ `duration`, **in milliseconds** where the per-set field is seconds

Summing `sets` and `reps` across each Activity's rollups also reproduces
`totalSets` and `totalReps` exactly.

That it reproduces is the finding: **the rollup carries no information the per-set
rows do not**, which is what makes it a check rather than a source, as
[ADR 0007](../../docs/adr/0007-prescription-needs-per-exerciseset-detail.md)
decided. It also closes that ADR's worked example. ADR 0007 observed that 4 of the
42 rollups carry a `volume` that is not `reps × maxWeight`, and that the
2026-09-07 trap bar deadlift — 3 sets, 9 reps, 930 000 g at a 110 000 g max —
fits both a 100/100/110 ramp and a 90 kg warm-up before two triples at 110.
`activity-service--exerciseSets__activity-24272601315.json` says it was
**100/100/110**. The rollup could not distinguish them; the per-set rows do.

### The Activity list totals — `totalSets`, `activeSets`, `totalReps` all present

All three are present and non-null on all 7 strength list entries in
`activitylist-service--search-activities__page-00.json`, and absent on all 8
cardio entries.

| Activity | `totalSets` | `activeSets` | `totalReps` | ACTIVE rows in the dump |
| --- | --- | --- | --- | --- |
| 23898645459 | 13 | 13 | 83 | 13 |
| 23930958725 | 15 | 15 | 71 | 15 |
| 24011353395 | 14 | 14 | 70 | 14 |
| 24098326007 | 18 | 18 | 88 | 18 |
| 24185357395 | 17 | 17 | 81 | 17 |
| 24272601315 | 16 | 16 | 83 | 16 |
| 24444892080 | 14 | 14 | 82 | 14 |

Two consequences. `totalSets` counts ACTIVE rows only — REST is excluded, so the
list totals are not inflated by rest. And **`activeSets` equals `totalSets` on
every Activity in the window**, so `activeSets` adds nothing; both include
CancelledExerciseSets, and neither can tell one from a completed set. The rollup
has no field that can.

### `hrTimeInZones` — shape, and one zone set

A flat array, one entry per zone, no envelope:

```json
[ { "zoneNumber": 1, "secsInZone": 591.39, "zoneLowBoundary": 116 }, ... ]
```

Five entries on all 8 cardio dumps, `zoneNumber` 1–5, the same floors
(116 / 128 / 141 / 154 / 167 bpm) on every one.

`biometric-service--heartRateZones.json` is also an array, one entry per
SportProfile, and holds exactly one for this athlete:

```json
[ { "sport": "DEFAULT", "trainingMethod": "HR_RESERVE", "restingHeartRateUsed": 51,
    "maxHeartRateUsed": 180, "lactateThresholdHeartRateUsed": null,
    "zone1Floor": 116, "zone2Floor": 128, "zone3Floor": 141,
    "zone4Floor": 154, "zone5Floor": 167, "restingHrAutoUpdateUsed": false,
    "changeState": "UNCHANGED" } ]
```

So there is no per-sport override to get wrong in this corpus, and cross-sport
comparison is safe here — but that is this athlete's configuration, not a
property of the endpoint, which returns one entry per profile.

Two shape facts the array does not advertise. **There is no zone 0**: time below
`zone1Floor` is not reported anywhere, so the seconds never sum to the Activity's
duration. The shortfall runs from 40 s on a 3 684 s run
(`activity-service--hrTimeInZones__activity-23965663197.json`) to 488 s on a
1 393 s run (`...24211302187.json`). And **zone 5 has a floor and no ceiling**;
`maxHeartRateUsed` 180 is the configured maximum, not a cap on the data — one run
in the window records a `maxHR` of 183.

Both the shape and the redundancy with the list entry are already decided in
[ADR 0008](../../docs/adr/0008-one-heart-rate-zone-set.md), written from these
dumps: the list entry's `hrTimeInZone_1`–`hrTimeInZone_5` are identical to
`secsInZone` on all 8 Activities, so the sync reads the seconds off the list,
fetches the boundaries once per run, and never calls `hrTimeInZones`.

## The one Activity edited in Garmin Connect, and what that costs

`activity-service--exerciseSets__activity-23930958725.json` (2026-08-10) differs
from the other six in every structural respect at once:

| | that Activity | the other six |
| --- | --- | --- |
| `exercises[]` length | 1 | 3 |
| `probability` | `100.0` | `99.609375` |
| `wktStepIndex` | `null` on all 30 rows | populated but for one row each |
| `messageIndex` | `null` on all 30 rows | populated (except all of 23898645459) |
| REST `weight` | `-1.0` | `null` |
| REST `startTime` | `null` | populated |

The list entry gives no hint: same `deviceId`, `manufacturer: GARMIN`,
`manualActivity: false`, `workoutId` populated. **The athlete confirms they edited
this session in Garmin Connect**, so the table above is the signature of a
Connect-side edit rather than an anomaly to explain away. That makes the two
kinds of correction distinguishable in the payload:

| | corrected on the watch | corrected in Garmin Connect |
| --- | --- | --- |
| how it shows | within-step variation, structure intact | the structural signature above |
| `wktStepIndex` | preserved | gone |
| `exercises[]` | 3 ranked candidates | 1 candidate at exactly `100.0` |
| `messageIndex` | preserved | gone |

**The cost is the block grouping, not Adherence.** A Connect edit strips
`wktStepIndex` from every row, so that Activity's sets cannot be gathered into
the blocks they were performed in, and the correction signal above cannot be read
from it at all. Adherence is unaffected, because Adherence was never measured
against Garmin — see the next section.

Two consequences follow, one for the code and one for the athlete.

For the code: the signature is a usable **detector**. An Activity whose rows all
carry one candidate at probability 100.0 with no `wktStepIndex` was edited after
recording, and any per-block reading of it — set-to-set decay, a weight ramp, a
cancelled-then-resumed block — should be reported as unavailable rather than as
absent. Those are different claims: the first says the shape cannot be seen, the
second says the session had no shape.

For the athlete: **correcting on the watch preserves more than correcting in
Connect.** Both leave the sets, the reps and the weights intact, so the volume
and the per-Exercise history are the same either way. What a Connect edit costs
is the within-session shape — which sets belonged together, and in what order
they were actually attempted. Worth knowing, and a good deal narrower than it
looked before this note was corrected.

It also explains the one value in the corpus that is hard to credit — `CHOP` /
`CABLE_ROTATIONAL_LIFT` at 8 reps of 12 000 g followed by 8 reps of **85 000 g**,
where that movement is 12–12.5 kg in every other Activity, inflating the
Activity's rollup volume for it to 776 000 g. A hand edit against a long
drop-down is a far better explanation than an 85 kg cable rotational lift, but
either way it is committed data and the store will hold it. **An implausible
weight is not something the ingest boundary should reject**: it has no way to
know an athlete's plausible range, and silently dropping a real lift is worse
than keeping an odd one.

## What `wktStepIndex` is for, and what it is not

This note originally treated `wktStepIndex` as a join onto the prescribed
Workout, and described a Connect-edited Activity as having no Adherence to
report. **Both were wrong**, and the reason is worth recording because it would
otherwise get rediscovered as a bug.

**`workoutId` is not a foreign key the store can resolve.** A Garmin Workout is
mutable and deletable, and this athlete reuses a small number of them — the seven
strength Activities in the corpus reference just two, `1675565879` and
`1535387640`, one of which covers four sessions spread over four weeks. Editing
either Workout changes what a past Activity appears to have been prescribed,
because the Activity holds only the id. Deleting one leaves the id pointing at
nothing. And the decade of history in the bulk export will reference Workouts
that are long gone or long since rewritten. Even a same-day sync can lose the
race against an edit.

So **the prescribed target must come from our own store, never from Garmin.**
That is what [CONTEXT.md](../../CONTEXT.md) already says — Adherence is measured
against ScheduledWorkouts where they exist and against the Phase's weekly
allocation where they do not — and what
[ADR 0002](../../docs/adr/0002-own-datastore-is-the-system-of-record.md) settled.
An Activity edited in Connect still has full Adherence against a ScheduledWorkout
we wrote.

`wktStepIndex` keeps a narrower and entirely self-contained job: **it groups the
sets of one block within one Activity.** No Workout lookup, no cross-Activity
assumption, just an index in the payload being read.

That job cannot be done any other way, which is what makes it worth storing.
Sets are **not recorded in step order.** From
`activity-service--exerciseSets__activity-24098326007.json`, the ACTIVE rows in
`messageIndex` order:

| `messageIndex` | `wktStepIndex` | Exercise | reps |
| --- | --- | --- | --- |
| 13, 15 | 7 | `DUMBBELL_SPLIT_SQUAT` | 6, 6 |
| 17 | 10 | `SEATED_CABLE_ROW` | **0** |
| 18, 20, 22 | 13 | `BENCH_PRESS` | 5, 5, 5 |
| 24 | 16 | `CABLE_ROTATIONAL_LIFT` | **0** |
| 25, 27, 29 | 10 | `SEATED_CABLE_ROW` | 6, 6, 6 |
| 31, 33 | 16 | `CABLE_ROTATIONAL_LIFT` | 8, 8 |

The athlete started step 10, cancelled it, did step 13, started step 16,
cancelled it, came back to finish step 10, then came back to finish step 16.
Grouping by consecutive runs of the same Exercise would read that as two separate
blocks of rows and two separate blocks of chops, and would read each cancelled
set as a standalone event rather than as the start of a block that was resumed
later. Only `wktStepIndex` recovers it.

It is also what separates a skipped block from a deferred one. Five of the eight
CancelledExerciseSets in the corpus have working sets at the same step later in
the session; two do not. Without the index, all eight read the same way.

It also separates two blocks of the *same* Exercise, which nothing else does. In
that same Activity the glute bridge appears at step 1 as 8, 8, 8 and at step 4 as
1, 1, 1 — two different pieces of work. `summarizedExerciseSets` merges them into
one entry of 6 sets and 27 repetitions, losing the distinction entirely, which is
one more count against the rollup.

**`workoutId` keeps one job too**, and only one: non-null means the session
followed a Workout, which is the Freestyle distinction in CONTEXT.md. That is a
boolean about provenance and it stays true after the Workout it names is deleted.
Storing the id alongside it costs nothing and may help a human reading a row, but
nothing should be built on being able to fetch it.

There is one exception, and it arrives for free in
[step 8](../../docs/build-order.md). When *we* create the Garmin Workout from our
own ScheduledWorkout, we know both ends at that moment and can record the
mapping. From then on a returning Activity's `workoutId` resolves to a
ScheduledWorkout in our store, whose target is immutable because we own it. The
join becomes reliable not by reading Garmin more carefully but by never needing
to read it back — which is
[ADR 0002](../../docs/adr/0002-own-datastore-is-the-system-of-record.md) doing
exactly the job it was written for.

## Which project this implies

**Fork 1.** `weight` is present, in a known unit, at plate-increment precision;
Exercise identity is present on every row bar one; and the athlete's per-set
corrections are in the API. The strength analytics are viable on this data as it
stands. The bulk export is valuable for backfilling years of the same, and Garmin
write-back stays where it is — a convenience that automates a manual step, last in
the order because it is the only step that can damage anything.

Fork 2 is ruled out for this athlete's recorded history, with two honest limits.
Every strength Activity in the window followed a pushed Workout, so the claim is
about the way this athlete actually trains and not about freestyle sessions, of
which the corpus holds none. And the per-set corrections are only *joinable* to a
prescription while the Activity is left as the watch recorded it — the one
Connect-edited session in the corpus has no `wktStepIndex` on any row.

### Does `docs/build-order.md` change?

**No reordering.** Step 1 is done — its "done when" asks whether `weight` is
populated, whether `exercises[].name` is a real Exercise or `UNKNOWN`, whether the
on-watch corrections survived, and whether `summarizedExerciseSets` is ever
non-empty. All four are answered above from named dumps. Steps 2 through 8 keep
their order and their content.

Two wording changes fall out of the findings, and are made in the same change as
this note:

- **Step 8's "done when"** asked for the resulting Activity to come back "with
  exercise names attached". As written that is unfalsifiable for this athlete's
  most frequent lift, whose name is `null` by design. It now asks for the
  Exercise — the category, and a name where the Exercise has one.
- **The Layout block** gains `fixtures/`, which this change creates and which
  both test seams read.

[ADR 0007](../../docs/adr/0007-prescription-needs-per-exerciseset-detail.md) and
[ADR 0008](../../docs/adr/0008-one-heart-rate-zone-set.md) already absorbed the
call-shape consequences of this corpus, so there is nothing left for the build
order to restate.

Nor does the corrected reading of `wktStepIndex` change it. Step 7 already writes
ScheduledWorkouts into our own store and step 8 already creates the Garmin
Workout from one, so the build order never depended on reading a prescription
back out of Garmin. It was this note that had drifted, not the plan.

[CONTEXT.md](../../CONTEXT.md) changes in three places, each a correction the
corpus or the athlete forced:

- **Exercise** was "identified by a category and a name". The name is optional,
  and null on this athlete's most frequent lift.
- **ExerciseSet** gains bodyweight as a stated load, distinct from a set
  performed with no load at all.
- **Workout** gains the step-and-repeat structure, which is what makes a
  within-step difference readable as a correction — and the warning that a
  Workout is mutable, so naming one is evidence a session was structured, never a
  route back to what it asked for.
- **CancelledExerciseSet** said it was evidence a step "was deliberately not
  done". Five of the eight in the corpus were resumed later in the same session,
  so it marks a block begun and stopped, and the skip-or-deferral question is
  answered by the rest of the Activity.

## What the ingest boundary has to do defensively

Collected here because every item is an observation above rather than a policy,
and each one is a line of code somewhere in step 3 or step 4.

- Read the Exercise as **(`category`, optional `name`)**. A null name is an
  answer.
- Take the **first** entry of `exercises[]`, not all of them, and keep its
  `probability`. An empty array means REST. Candidates can disagree, and when
  they do the top one can be `UNKNOWN`.
- Treat `weight` as grams; store it as an integer. The three non-positive values
  are three different answers: **`-1` is bodyweight**, `0` is performed with no
  external load, `null` is nothing performed. Never treat `-1` as a gram count,
  and never collapse it into `0`.
- Treat `repetitionCount == 0` on an ACTIVE row as a CancelledExerciseSet:
  excluded from volume and from per-set averages, and never detected by duration.
  Do not read one as skipped work — check whether its `wktStepIndex` has working
  sets elsewhere in the Activity, because most of them are deferrals that were
  resumed.
- Never assume `wktStepIndex` or `messageIndex` is present. Both are null on rows
  in six of seven dumps and on every row of one. Where it is present, store it:
  it is the only thing that groups the sets of a block, and sets are not recorded
  in step order. Where it is absent, the block structure is unavailable — which
  is not the same as the session having had none.
- Store `workoutId` as provenance, and never resolve it. Non-null means the
  session followed a Workout; the Workout itself may have been rewritten or
  deleted since, so the prescribed target comes from our own ScheduledWorkout.
- Keep the whole `exercises[]` array, or at least the candidate count and the top
  probability. One candidate at exactly `100.0` across every row, with
  `wktStepIndex` absent, is the signature of an Activity edited in Garmin Connect
  rather than recorded, and that is worth knowing at query time.
- Do not reject an implausible `weight`. The boundary cannot know an athlete's
  range, and dropping a real lift is worse than storing an odd one.
- Per-set `startTime` is **GMT with no offset marker** — `2026-09-21T14:34:01.0`
  against a list `startTimeGMT` of `2026-09-21 14:34:01` and a `startTimeLocal`
  of `16:34:01`. Parsing it as local time moves every ExerciseSet by the offset.
  It is also null on the REST rows of one dump.
- `summarizedExerciseSets` `duration` is **milliseconds**; the per-set `duration`
  is **seconds**.
- `totalSets` excludes REST and includes cancelled sets; `activeSets` is
  indistinguishable from it and earns no column.
- An absent `hrTimeInZone_N` is unknown, never zero seconds, and there is no
  zone 0 — the seconds are not expected to sum to the Activity's duration.

## What was scrubbed, and what was not

**The raw payloads are not safe to commit.** Only the Activity list carries
identifying fields, and it carries a lot of them: the athlete's full name,
Garmin's account id and profile UUID, three public profile-image URLs with the
account id embedded, the watch's `deviceId`, the account's full OAuth scope list,
free-text Activity names naming an injury and opposing football clubs, a
`locationName`, and `startLatitude` / `startLongitude` / `endLatitude` /
`endLongitude` at roughly metre precision — which locate a home address and a set
of training grounds. The repository has a public remote. So: scrubbed, and the
decision is recorded here rather than assumed.

`exerciseSets`, `hrTimeInZones` and `biometric-service--heartRateZones.json`
carry **no** identifying field — a full key census of all three found only the
metric fields — and are committed as the bytes Garmin sent, unmodified.

[`scrub_dumps.py`](scrub_dumps.py) does the work, so the rules are readable and
re-runnable rather than a one-off edit:

| field | becomes | why |
| --- | --- | --- |
| `ownerFullName` | `"Athlete"` | the athlete's real name |
| `ownerId` | `1` | account identifier, and embedded in the image URLs |
| `ownerDisplayName` | a fixed placeholder UUID | account identifier |
| `ownerProfileImageUrl{Small,Medium,Large}` | `https://example.invalid/profile.png` | publicly fetchable, carries the account id |
| `deviceId` | `1`, but `0` kept | device identifier; `0` is the absence of a device, not an id |
| `activityUUID` | a placeholder UUID, one per Activity | per-Activity identifier |
| `userRoles` | `["ROLE_CONNECTUSER"]` | the account's full entitlement list |
| `activityName` | the Activity's `typeKey` | free text stating an injury and naming clubs |
| `locationName` | `"Somewhere"` | locates the session |
| `start`/`end` `Latitude`/`Longitude` | `null` | locates the session to the metre |

Placeholders are **stable, not random**: one account is one placeholder in every
file, so a fixture that joins an Activity to its owner still joins, and
re-running the scrubber over the same run produces the same bytes rather than a
diff. Null stays null and `0` stays `0`, so no shape is invented that Garmin did
not send.

**Deliberately kept:**

- **`activityId` and `workoutId`.** Neither is an account, profile or device
  identifier, and both are useless without authentication. Renumbering them would
  break the join between this corpus, its `index.json`, the coverage notes beside
  this file, and the athlete's own Connect account — which is the join a follow-up
  question needs.
- **Timestamps, and `timeZoneId`.** Dates are load-bearing for the fixtures:
  windows, watermarks and the ordering the sync is tested against. A timestamp
  with the coordinates removed locates nothing.
- **Physiological values** — `restingHeartRateUsed` 51, `maxHeartRateUsed` 180,
  the zone floors, every weight and repetition count. This is the data the corpus
  exists to carry, and detached from identity it identifies nobody.

The guarantee is enforced rather than asserted. `scrub_dumps.py` stages its
output and publishes nothing unless three checks pass, each naming what stopped
it and leaving the output directory untouched:

1. **Every file is a kind the rules were written for.** An unrecognised file is
   refused rather than copied. This is what keeps an `ABORT-*.txt` out of the
   corpus: it holds the request URL and the first 2 000 bytes of Garmin's error
   body, and copying it needs no rule to be wrong to leak.
2. **There is an Activity list to build the census from.** Without one the census
   is empty and every value check passes vacuously — the failure mode a human is
   least likely to notice, because the tool reports success.
3. **Every scrubbed field holds what the rule table says it should**, checked
   value by value rather than by searching for the raw value, so a one-character
   Activity name is handled as exactly as a full name. On top of that, every
   staged file is swept for any raw value long enough to search for, and each
   copied payload is refused if it has grown a field the Activity list scrubs —
   because "these three payload types carry no identifying field" is a fact about
   a shape Garmin can change, not a permanent property.

[`test_scrub_dumps.py`](test_scrub_dumps.py) drives all three, including the case
where a rule is missing altogether, which is the way a leak would actually
happen. It is the only spike code with tests beyond the pacing and GET-only
cases, because it is the only spike code whose output git keeps. An independent
sweep of the committed corpus for every real identifier returned nothing.

## Nothing here needs re-fetching

The raw dumps stay on disk under `spikes/strength-data-spike/dumps/`, gitignored,
one directory per run. Every figure in this note was recomputed from the
**committed** corpus, so a follow-up question about any field above is answerable
from the repository alone. A question about the four scrubbed coordinate fields or
the two scrubbed names is answerable from the raw run on the athlete's machine.
Neither needs Garmin.

## What the dumps could not answer, and the athlete did

Three claims in this note rest on the athlete rather than on the payloads, and
are marked as such where they appear:

- **How a Workout step works** — one Exercise, one target, a repeat count — which
  is what makes within-step variation proof of a correction rather than a
  curiosity. Nothing in a response says this; it is a property of the tool the
  Workout was built in.
- **That the 2026-08-10 session was edited in Garmin Connect**, which turns a
  structural oddity into a detector for edited Activities.
- **That Garmin Workouts are edited and deleted freely.** The corpus shows the
  reuse — two `workoutId`s across seven Activities, one of them spanning four
  weeks — but not the churn, and it is the churn that makes `workoutId`
  unresolvable and sends the prescribed target to our own store instead.

Each was an open question when this note was first written, and the third
corrected a claim the note had already made. They are recorded here so the
distinction between what was observed and what was reported survives; everything
else above is read off the committed corpus and recomputable from it.
