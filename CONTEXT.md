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

**Volume**:
The load a strength Activity's working ExerciseSets lifted: repetitions times
weight in kilograms, summed. A CancelledExerciseSet never counts towards it, and
a bodyweight ExerciseSet adds nothing to it, because its load is stated as the
athlete's own mass rather than in kilograms — what that mass was is decided
where the figure is read.
_Avoid_: tonnage, workload, total weight

**StrengthDetail**:
One strength Activity's stored ExerciseSets in recorded order, together with the
figures they support: working and cancelled set counts, Volume, and the average
repetitions per working set. CancelledExerciseSets appear in the rows and in
their own count, and in no figure.
_Avoid_: set summary, strength breakdown, workout detail

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
each, measured against the SportProfile revision it was first stored against.
_Avoid_: zone, HR zone, intensity zone, band

**SportProfile**:
The sport a set of HeartRateZone boundaries is configured for. An athlete has a
default profile and may override it per sport, so time in zones is only
comparable across sports once the profile behind each Activity is known. Its
boundaries are kept as observed on a date: a change adds a revision rather than
replacing one, and a Cardio Activity keeps the revision it was first stored
against.
_Avoid_: sport, sport type, zone set, profile

**CardioDetail**:
One Cardio Activity's time in each HeartRateZone, together with the SportProfile
revision it is measured against and the date those boundaries were observed —
which is not a claim that they were the boundaries in force when it happened.
_Avoid_: zone summary, intensity breakdown, cardio summary

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
The accumulated cost of recent Activities, carried on two axes because no single
instrument sees both: a systemic axis measured from heart rate, and a strength
axis counted in working ExerciseSets. The systemic axis is Garmin's weekly
training load — seven days of heart-rate-derived recovery cost — against its load
tunnel, the range Garmin's own model judges optimal for the athlete's recent
fitness and training. Read as load progression, never as a target. The strength
axis compares a short window (acute) against a long one (chronic).
_Avoid_: stress, TSS, volume, workload

**VO2max**:
Garmin's estimate of the athlete's maximal oxygen uptake in ml/kg/min, one per
sport (generic from running, and cycling), dated by the day Garmin computed it
rather than read daily. Read as a trend over weeks.
_Avoid_: fitness, VO2, aerobic capacity

**EstimatedOneRepMax**:
The heaviest single repetition a working ExerciseSet implies for its Exercise,
estimated from repetitions and weight. Read as a trend within one rep scheme: a
change of scheme steps the estimate without the strength changing.
_Avoid_: 1RM, e1RM, one-rep max, PR

**Adherence**:
The degree to which the Activities in a date range match what was prescribed for
that range: measured against ScheduledWorkouts where they exist, and against the
WeekOutline's modality allocation where they do not.
_Avoid_: compliance, plan vs actual, completion

### Daily wellness

**SleepNight**:
One night's recorded sleep, attributed to the calendar date it ends on, so a
night beginning on the evening of the 6th belongs to the 7th. Carries start,
end, duration and the minutes in each stage. Duration reads as an upper bound on
a fragmented night.
_Avoid_: sleep, sleep session, sleep record, sleep day

**BodyBattery**:
The day's energy reserve as Garmin models it — charged, drained, highest and
lowest. Carried on the DailySummary rather than fetched on its own, because the
dedicated endpoint adds only an intraday series.
_Avoid_: energy, battery, reserves

**StressLevel**:
The day's autonomic stress as Garmin models it — average, maximum and the
durations at each level. Carried on the DailySummary, for the same reason as
BodyBattery.
_Avoid_: stress, strain, load

**DailySummary**:
Garmin's per-day rollup for one calendar date, carrying BodyBattery and
StressLevel among its figures. One call per day, and the reason the dedicated
body battery and stress calls are not made.
_Avoid_: user summary, daily stats, wellness day

### Sync

**ActivityRange**:
An inclusive pair of calendar dates an IngestBatch covers. The sync posts one
day per batch, whether the Watermark chose the day or the athlete named it, and
the range travels with its batch so a successful empty day still has a recorded
sync time.
_Avoid_: window, period, date span

**IngestBatch**:
A versioned, atomic delivery to the store. Version 2 carries one ActivityRange,
its Activities (possibly none), and optional AthleteProfile changes. Version 3
adds the range's SleepNights and DailySummaries, either possibly none, and states
that every date in the range was synced, so a date with neither is one Garmin
held nothing for rather than one never attempted. Version 4 adds each date's
systemic TrainingLoad and the VO2max estimates delivered with it, and states that
every date was synced for those as well; a VO2max estimate keeps the date Garmin
computed it, which may precede the range. Replaying identical content is the
same delivery and does not advance freshness.
_Avoid_: upload, payload

**SyncAlert**:
One push notification about a run that gave up, sent from the home machine to the
athlete's phone (ADR 0001). Three kinds, deliberately distinct: a failure, naming
what stopped the run and when, and one for each sign-in that can fall due — Garmin's
roughly annual interactive login and the SyncSignIn — each naming what the athlete
must sit down and do, and where. A fault that healed on retry is none of them, and
nor is a run turned away because another holds the SyncSlot.
Azure never sends one, because it cannot tell a failed sync from a machine that
is switched off.
_Avoid_: alert, error, push, warning, reminder

**SyncStatus**:
The stored record count and last successful sync time for each collection, plus
the most recent Activity summary when one is stored, and the latest date a
version 3 or later IngestBatch reached with the kinds of record that date holds — none,
for a date Garmin held nothing for.
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

**SyncSlot**:
The home machine's permission to run one sync at a time, held for the length of a
run. A run that cannot take it stops without fetching anything, because the run
already holding it covers the same days. It is an operating-system lock, so it is
released however a run ends.
_Avoid_: mutex, semaphore, pid file, guard, singleton

**SyncSignIn**:
The home machine's own saved sign-in to the service that issues ingest access
tokens, obtained interactively once per machine and kept as a refresh token the
sync exchanges for as many short-lived access tokens as a run needs (ADR 0013).
Distinct from the athlete's Garmin login and from a chat client's connection: it
falls due separately and is renewed in a different place.
_Avoid_: API key, service account, machine token, credentials
