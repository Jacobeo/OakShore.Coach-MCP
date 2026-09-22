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
_Avoid_: session, workout, training

**ExerciseSet**:
One set within a strength Activity, either working or resting, carrying
repetitions and weight lifted.
_Avoid_: set, rep set, lift

**Exercise**:
The movement performed in an ExerciseSet, identified by a category and, where the
category has variants, a name. A category with no name is a whole Exercise, not a
missing one: the flat bench press is `BENCH_PRESS` with no name at all.
_Avoid_: lift, movement, drill

**CancelledExerciseSet**:
An ExerciseSet the athlete started and cancelled to skip or defer a step of a
Workout, which the watch requires: it records zero repetitions against a weight.
Evidence that a prescribed step was deliberately not done, so it counts towards
Adherence and never towards volume or a per-set average.
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
intensities. A description of work to be done, never work that was done.
_Avoid_: session, planned workout, template

**ScheduledWorkout**:
A Workout assigned to a specific date, so that it can be followed on that day.
_Avoid_: calendar item, planned session, booking

**TrainingPlan**:
An ordered series of ScheduledWorkouts covering a period of weeks. Garmin's unit
of structured coaching; detailed down to individual steps.
_Avoid_: program, schedule, training block

### Planning

**Program**:
The multi-month structure of an athlete's training: a goal, a date range, and an
ordered series of Phases. Describes intent and proportion rather than individual
sessions, and has no Garmin equivalent.
_Avoid_: plan, training plan, periodization, macrocycle

**Phase**:
One named stage of a Program, covering a date range, with a stated focus and an
allocation of training across modalities per week.
_Avoid_: block, mesocycle, month, cycle

**Goal**:
An outcome the athlete is training towards, with a target date. A Program exists
to serve a Goal.
_Avoid_: objective, target, race

**Constraint**:
A temporary limitation on an athlete's training, covering a stated period, such
as an injury or a week with less time available. Unlike an AthleteProfile fact,
a Constraint expires.
_Avoid_: note, restriction, injury, availability

**AthleteProfile**:
The facts about an athlete that a prescription depends on but that training data
does not reveal: body weight, available equipment, time available to train, and
injuries or limitations.
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
Phase's weekly allocation where they do not.
_Avoid_: compliance, plan vs actual, completion
