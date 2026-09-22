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

The clearest single piece of evidence, from
`activity-service--exerciseSets__activity-24185357395.json` — one prescribed
Workout step, `wktStepIndex` 5, three ExerciseSets:

| reps | weight |
| --- | --- |
| 3 | 95 000 g |
| 3 | 100 000 g |
| 3 | 105 000 g |

One step of one Workout, three different weights, ascending. Garmin has no way
to hold those three numbers unless the athlete entered them on the watch while
lifting. `activity-service--exerciseSets__activity-24272601315.json` shows the
same at `wktStepIndex` 4 — 3×100, 3×100, 3×110 kg — and
`activity-service--exerciseSets__activity-24444892080.json` shows repetitions
moving within a step at a fixed weight: 8, 4, 4, 0 at 75 000 g under
`wktStepIndex` 4.

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

Three values are not weights and must not be ingested as such:

| value | rows | what it is |
| --- | --- | --- |
| `null` | 94 | 92 REST rows, plus 2 ACTIVE rows |
| `-1.0` | 18 | 15 REST rows in one Activity, plus 3 ACTIVE `STANDING_CALF_RAISE` rows |
| `0.0` | 12 | ACTIVE, banded and bodyweight movements |

`-1.0` is a sentinel for "no weight recorded", not a weight. It appears on ACTIVE
rows (`activity-service--exerciseSets__activity-23898645459.json`, the three
`STANDING_CALF_RAISE` sets at `wktStepIndex` 15) and on every REST row of
`activity-service--exerciseSets__activity-23930958725.json`. Garmin's own rollup
treats it as zero: that Activity's `CALF_RAISE` entry in
`summarizedExerciseSets` reports `maxWeight` 0 and `volume` 0 against 25
repetitions. **A negative `weight` must be read as unknown, and never
arithmetically.** `0.0`, by contrast, is a real answer — a banded glute bridge
carries no external load.

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

Seven of the eight join to a prescribed step through `wktStepIndex`, so they are
usable Adherence evidence exactly as [CONTEXT.md](../../CONTEXT.md) describes.
The eighth is the unclassifiable `UNKNOWN` row, which has no `wktStepIndex` and
so evidences nothing beyond itself.

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
- `maxWeight` = max(`weight`, 0) — so the `-1.0` sentinel floors to 0
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

## One Activity that does not look like the others

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

The list entry gives no explanation: same `deviceId`, `manufacturer: GARMIN`,
`manualActivity: false`, `workoutId` populated. And its data carries one value
that is hard to credit — `CHOP` / `CABLE_ROTATIONAL_LIFT` at 8 reps of 12 000 g
followed by 8 reps of **85 000 g**, where the same movement is 12–12.5 kg in every
other Activity in the corpus. That single value inflates the Activity's
`summarizedExerciseSets` volume for the movement to 776 000 g.

A single candidate at probability exactly 100.0, with the Workout step and
message indices gone, is what one would expect from sets rewritten after the fact
rather than recorded live. **That is inference, not observation, and the dumps
cannot settle it.** It is the one question in this note that needs the athlete
rather than the data — see the note at the end.

Its practical weight is low either way: it is 1 Activity in 7, and the
`wktStepIndex: null` case it exhibits has to be handled regardless, because the
other six each carry one such row too.

## Which project this implies

**Fork 1.** `weight` is present, in a known unit, at plate-increment precision;
Exercise identity is present on every row bar one; and the athlete's per-set
corrections are in the API. The strength analytics are viable on this data as it
stands. The bulk export is valuable for backfilling years of the same, and Garmin
write-back stays where it is — a convenience that automates a manual step, last in
the order because it is the only step that can damage anything.

Fork 2 is ruled out for this athlete's recorded history, with one honest limit:
every strength Activity in the window followed a pushed Workout, so the claim is
about the way this athlete actually trains and not about freestyle sessions, of
which the corpus holds none.

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

[CONTEXT.md](../../CONTEXT.md) changes in one place: **Exercise** was defined as
"identified by a category and a name", and the corpus says the name is optional.

## What the ingest boundary has to do defensively

Collected here because every item is an observation above rather than a policy,
and each one is a line of code somewhere in step 3 or step 4.

- Read the Exercise as **(`category`, optional `name`)**. A null name is an
  answer.
- Take the **first** entry of `exercises[]`, not all of them, and keep its
  `probability`. An empty array means REST. Candidates can disagree, and when
  they do the top one can be `UNKNOWN`.
- Treat `weight` as grams; store it as an integer. **Negative is unknown**, null
  is unknown, `0` is a real unloaded set.
- Treat `repetitionCount == 0` on an ACTIVE row as a CancelledExerciseSet:
  counted for Adherence, excluded from volume and from per-set averages. Never
  use duration to detect one.
- Never assume `wktStepIndex` or `messageIndex` is present. Both are null on rows
  in six of seven dumps and on every row of one.
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

## The one open question

Whether `activity-service--exerciseSets__activity-23930958725.json` — the
2026-08-10 session — had its sets edited in Garmin Connect after the fact. The
dumps show the signature and cannot show the cause, and the answer decides whether
"single candidate at probability 100.0, no `wktStepIndex`, no `messageIndex`" is a
marker the sync can use to spot edited Activities, or just one odd recording. It
changes nothing above either way.
