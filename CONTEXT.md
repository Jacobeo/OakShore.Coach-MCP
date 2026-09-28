# Training Coach

A personal training assistant that holds an athlete's training history, plan and
context in one place, so that a chat model can reason about them together and
prescribe the next block of training. Garmin Connect is an upstream source of
recorded training and a downstream destination for prescribed workouts; it is
not the system of record.

## Language

### Completed training

**Activity**:
A single completed, recorded training session. The unit of "what I actually did".
Its name and activity type describe the whole Activity; individual Exercise names
belong to its ExerciseSets.
_Avoid_: session, workout, training

**ExerciseSet**:
One set within a strength Activity, either working or resting, carrying
repetitions and weight lifted. A working set may be performed at bodyweight,
which is a stated load rather than a missing one, and is distinct from a set
performed with no load at all.
_Avoid_: set, rep set, lift

**Exercise**:
The movement performed in an ExerciseSet, identified by a category and, where the
category has variants, a name. A category with no name is a whole Exercise, not a
missing one: the flat bench press is `BENCH_PRESS` with no name at all.
_Avoid_: lift, movement, drill

**CancelledExerciseSet**:
An ExerciseSet the athlete started and cancelled, which the watch requires in
order to skip or defer a step of a Workout: it records zero repetitions against a
weight. It marks a block begun and stopped, not work missed — the same step is
usually completed later in the session — so whether it was a skip or a deferral
is answered by the rest of the Activity, never by the row alone. Never counts
towards volume or a per-set average.
_Avoid_: empty set, failed set, zero set, bad data

**Freestyle**:
Said of an Activity recorded without a Workout to follow. The word exists to
name the case this athlete does not train in; the other case is an Activity
that *followed* a Workout, and that phrase is the term for it.
_Avoid_: unstructured, ad-hoc, unplanned, off-plan

**Cardio**:
Said of an Activity whose intensity is measured as time in HeartRateZones
rather than in ExerciseSets — running and cycling among them. Defined as the
complement of a strength Activity rather than as a list of sports, so a type
nobody anticipated is never silently dropped.
_Avoid_: endurance, aerobic, conditioning

**HeartRateZone**:
One band of the athlete's heart-rate range, identified by its number and its
lower boundary in beats per minute. A cardio Activity records the time spent in
each, and carries the boundaries that were in force when it happened.
_Avoid_: zone, HR zone, intensity zone, band

**SportProfile**:
The sport a set of HeartRateZone boundaries is configured for. An athlete has a
default profile and may override it per sport, so time in zones is only
comparable across sports once the profile behind each Activity is known.
_Avoid_: sport, sport type, zone set, profile

### Prescribed training

**Workout**:
A reusable structured definition of a training session: its steps, targets and
intensities. A description of work to be done, never work that was done. One step
names a single Exercise with one target — repetitions and weight — and how many
times to repeat it, so every ExerciseSet of a step shares one target. Recorded
sets that differ within a step therefore record a correction the athlete made
while training. A Workout is mutable and deletable, so a recorded Activity naming
one is evidence that it followed *a* Workout, never a way to recover what that
Workout asked for.
_Avoid_: session, planned workout, template

**ScheduledWorkout**:
A Workout assigned to a specific date, so that it can be followed on that day.
_Avoid_: calendar item, planned session, booking

**TrainingPlan**:
An ordered series of ScheduledWorkouts covering a period of weeks. Garmin's unit
of structured coaching; detailed down to individual steps.
_Avoid_: macrocycle, program, schedule, training block

### Planning

**Macrocycle**:
The long-range training structure serving an ordered snapshot of Goals, highest
priority first: a date range and an ordered series of Mesocycles. Its length
follows their shared deadline rather than a calendar year.
_Avoid_: program, plan, training plan, season

**Mesocycle**:
A focused training period within a Macrocycle, with a date range, intended
adaptation, and ordered WeekOutlines that shape progression and recovery.
_Avoid_: phase, block, month, cycle

**WeekOutline**:
One dated week's intended modality allocation and progression or recovery focus
within a Mesocycle, before its ScheduledWorkouts are prescribed.
_Avoid_: microcycle, weekly schedule

**Microcycle**:
One prescribed training week shaped by a WeekOutline, consisting of its
ScheduledWorkouts and recovery days. It can be revised as the athlete's form or
Constraints change.
_Avoid_: weekly plan, schedule, training week

**Goal**:
An outcome the athlete is training towards. The AthleteProfile orders current
Goals by priority; a Macrocycle captures that order for its own training period.
_Avoid_: objective, target, race

**Constraint**:
A temporary limitation on an athlete's training, covering a stated period, such
as an injury or a week with less time available. Unlike an AthleteProfile fact,
a Constraint expires.
_Avoid_: note, restriction, injury, availability

**AthleteProfile**:
The facts about an athlete that a prescription depends on but that training data
does not reveal: ordered Goals with one shared target date, body weight,
available equipment, time available to train, and lasting limitations.
_Avoid_: user, settings, preferences, context

### Form and load

**TrainingLoad**:
The accumulated physiological cost of recent Activities, measured over a short
window (acute) and a long window (chronic).
_Avoid_: stress, TSS, volume

**TrainingReadiness**:
A daily assessment of how prepared an athlete is to absorb hard training,
derived from sleep, recovery, HRV and recent TrainingLoad.
_Avoid_: recovery score, freshness, form

**Adherence**:
The degree to which the Activities in a date range match what was prescribed for
that range: measured against ScheduledWorkouts where they exist, and against the
WeekOutline's modality allocation where they do not.
_Avoid_: compliance, plan vs actual, completion

### Sync

**ActivityRange**:
An inclusive pair of calendar dates explicitly requested for a manual Activity
sync. The range travels with its ingest batch so a successful empty range still
has a recorded sync time.
_Avoid_: window, period, date span

**IngestBatch**:
A versioned, atomic delivery to the store. Version 2 carries one ActivityRange,
its Activities (possibly none), and optional AthleteProfile changes. Replaying
identical content is the same delivery and does not advance freshness.
_Avoid_: upload, payload

**SyncStatus**:
The stored record count and last successful sync time for each collection, plus
the most recent Activity summary when one is stored.
A null time means the collection has never been synced; a non-null time with zero
records means a completed sync found none.
_Avoid_: sync health, sync state

**Watermark**:
The sync's own record, kept on the home machine, of the days it has completed.
A day is complete once its batch is accepted by ingest; the current day is
synced but never completed, so the next run syncs it again. A run without dates
works most recent first through the days, from a configured earliest date to
today, that the Watermark does not cover, and stops at a per-run ceiling.
_Avoid_: checkpoint, cursor, last-run time, high-water mark
