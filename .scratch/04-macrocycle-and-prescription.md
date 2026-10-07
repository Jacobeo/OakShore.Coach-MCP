# Spec 04 — Macrocycle and prescription

## Problem Statement

This is the use case that caused the project to exist, and nothing before it
delivers the actual value.

The athlete has prioritized Goals with a shared deadline — a seven-a-side football season starting in
April — and a six-month Macrocycle produced in an earlier AI conversation: six
roughly monthly Mesocycles, each with a stated adaptation and WeekOutlines for
cardio, strength, progression and recovery, shifting from an aerobic base to
match-specific conditioning and a taper.

That Macrocycle is a PDF. It cannot be queried, it cannot be compared against what
was actually done, and it is invisible to any tool. Meanwhile Garmin holds the
training history but has no concept of a multi-month periodized structure and no
way to export one, so even a perfect Garmin client could not answer the question
the athlete actually asks, which is: *given what I have done recently, my Macrocycle
and my current form, what should I do over the next week?*

Secondary questions follow the same shape: am I on track for my Goals, and where
should I change something to improve my VO2max?

Without this spec, the system is a well-built archive of training data. With it,
it is a coach.

## Solution

Model the Macrocycle, its Mesocycles, and each Mesocycle's ordered WeekOutlines in
the datastore, authored and revised conversationally. Add the tools that let a
model read them alongside recent training and current form, and write the coming
week's ScheduledWorkouts as a Microcycle.

Structure dates and each WeekOutline's modality counts so the system can resolve
the current week and measure Adherence. Keep the Mesocycle's adaptation and each
week's progression or recovery focus as prose. That intent lets the agent adjust
a Microcycle when form or a Constraint changes.

Adherence compares Activities against ScheduledWorkouts where they exist, and
against the WeekOutline's allocation where they do not, which covers historical
periods the coach never prescribed.

## User Stories

1. As the athlete, I want to paste my existing plan document and have it stored as a Macrocycle, so that six months of thinking is not trapped in a PDF.
2. As the athlete, I want to build a Macrocycle through conversation instead of a document, so that planning and recording are one activity.
3. As the athlete, I want a copy of my Macrocycle back in readable form whenever I ask, so that I am never locked into the tool.
4. As the athlete, I want each Mesocycle's adaptation and each week's progression or recovery focus preserved in the words they were written in, so that intent is not flattened into numbers.
5. As the athlete, I want to be told which Mesocycle today falls in, so that I know where I am in the six months.
6. As the athlete, I want to be told how far into the current Mesocycle I am, so that I can judge whether to push or consolidate.
7. As the athlete, I want to ask what I should do this week and get specific sessions, so that I do not have to interpret the Macrocycle myself.
8. As the athlete, I want prescriptions to respect the current WeekOutline's allocation and recovery intent, so that the week serves the Macrocycle rather than my mood.
9. As the athlete, I want prescriptions to take my recent training load into account, so that a hard block is not stacked on top of fatigue.
10. As the athlete, I want prescriptions to account for how I have slept, so that a bad week is not treated as a normal one.
11. As the athlete, I want prescribed sessions written straight into the store when I ask for a week, so that there is no extra confirmation step.
12. As the athlete, I want to change my mind afterwards and have the week rewritten, so that a prescription is a starting point rather than a commitment.
13. As the athlete, I want to say I have picked up an injury and get a revised week, so that I keep training around the problem rather than stopping.
14. As the athlete, I want a revised week to preserve the Mesocycle's intent by substituting sessions, so that a knock costs me a modality rather than the block.
15. As the athlete, I want to say I only have two evenings this week and get a shorter week that still prioritises correctly, so that reduced availability does not mean an abandoned plan.
16. As the athlete, I want a temporary Constraint to keep applying to next week's prescription until it expires, so that I do not have to repeat myself.
17. As the athlete, I want to ask what I am doing today and get a short answer, so that it is usable on a phone in the morning.
18. As the athlete, I want to ask what I am doing this week and see the prescribed sessions, so that I can plan around them.
19. As the athlete, I want to know how much of the last month I actually completed, so that I can see drift before it becomes a pattern.
20. As the athlete, I want to know whether I am on track for my prioritized Goals, so that I can correct early rather than discover the gap in April.
21. As the athlete, I want Adherence measured against specific prescribed sessions where they exist, so that the assessment is precise rather than a count.
22. As the athlete, I want Adherence measured against the WeekOutline's allocation for weeks that were never prescribed, so that my existing history is still assessable.
23. As the athlete, I want to see how my training splits across intensity, so that I can tell whether I am training the middle at both ends.
24. As the athlete, I want to know where to change something to improve my VO2max, so that I get a specific action rather than general advice.
25. As the athlete, I want my strength progression over months, so that I can see whether loads are actually increasing.
26. As the athlete, I want to compare a period against an earlier one, so that I can tell whether this winter is better than the last.
27. As the athlete, I want the coach to say when its data is stale, so that I can discount its confidence accordingly.
28. As the athlete, I want the coach to say when it lacks the data to answer, so that I am not given a confident guess.
29. As a developer, I want the tool count kept small, so that model tool selection does not degrade.
30. As a developer, I want the server never to call a language model, so that it stays deterministic, testable and free to run.
31. As the athlete, I want a lighter recovery week represented in the Macrocycle before workouts are scheduled, so that weekly prescription and later Adherence respect it.
32. As the athlete, I want several Goals ordered by priority, so that a weekly trade-off protects the higher-priority outcome.
33. As the athlete, I want my Goals to share the Macrocycle's end date, so that priorities point to one planning horizon rather than separate deadlines.

## Implementation Decisions

**The three cycles have distinct jobs.** A Macrocycle captures the AthleteProfile
Goals in priority order as agreed when the Macrocycle is saved, plus its own date range
and ordered Mesocycles. A Mesocycle targets an adaptation and carries dated,
ordered WeekOutlines. Each WeekOutline records intended modality counts and a
progression or recovery focus. A Microcycle is the prescribed week's
ScheduledWorkouts and recovery days, revised as conditions change; it needs no
separate stored object. Garmin's TrainingPlan is a series of dated, step-level
Workouts, so it remains distinct from these coaching concepts.

**A Macrocycle keeps its Goals.** Editing or reordering the AthleteProfile Goals
later does not silently retarget an existing Macrocycle. The athlete revises that
Macrocycle through conversation when priorities change, preserving the meaning
of older prescriptions and Adherence.

**Goals share one deadline.** AthleteProfile stores the ordered Goals and one
shared target date. The Macrocycle captures both; its end date equals that
date. Individual Goals have no target date. The highest-priority Goal guides
trade-offs when time, fatigue or a Constraint makes every outcome impossible to
serve equally.

**Structure only what must be computed.** Mesocycle and WeekOutline dates, order,
and each week's modality counts are structured because date resolution and
Adherence need them. Adaptation, progression, recovery and substitution intent
stay as prose, so a Constraint can change the prescribed work while preserving
why that work was chosen. Mesocycle length is flexible; approximately three to
six weeks is guidance, not a validation rule. WeekOutlines cover their
Mesocycle's dates in order without gaps or overlap.

**The model parses; the server stores.** Converting a pasted plan document into
Mesocycles and WeekOutlines is done by the client model, which is good at exactly
that, and the structured result is submitted through a tool. The server never calls a language
model. This keeps it deterministic, cheap, offline-testable, and free of
inference costs and API keys.

**Prescription writes directly.** A request to prescribe a week writes its
Microcycle's ScheduledWorkouts into the store immediately rather than proposing a draft for
confirmation. The conversation is the approval step; an additional confirmation
tool costs a round trip and a client approval prompt every time, and deleting an
unwanted ScheduledWorkout is trivial because nothing has reached the watch. A
status column can be introduced later if a draft state is ever wanted.

**Re-prescription is a first-class operation.** Prescribing over a period that is
already prescribed replaces it. Constraints supplied in conversation are persisted
with an expiry rather than being applied only to the call in hand, so that next
week's prescription still knows about this week's injury.

**Adherence is two-level.** Where ScheduledWorkouts exist for a date range,
Activities are matched against them. Where they do not, Activities are counted by
modality against the WeekOutline's allocation. The first is precise and comes
free because the coach itself wrote the prescriptions; the second covers history
that predates the coach.

**Intensity Adherence is defined but deferred.** The initial Adherence measure is
completion. Its intensity dimension — whether the Activity that followed an easy
prescription was actually easy — compares a cardio Activity's time in
HeartRateZones against the prescribed intent: easy is zones 1–2, hard is 4–5,
and zone 3 is the middle worth flagging. It applies only where a
ScheduledWorkout stated an intent; a Freestyle or historical Activity shows its
distribution and is never judged, because inferring intent is the confident
guess user story 28 forbids. It is built once completion Adherence is live and
shows drift; the zone data captured in spec 03 serves it either way.

**Current form is shaped so the right reading is the only one expressible.** The
production client learns only from tool descriptions and payloads, so the
interpretation rules live in the response shape
([ADR 0009](../docs/adr/0009-trainingload-has-two-axes.md)): wherever a trusted
range exists, a field is a triple — status, value and the range — and a bare
number never travels alone. TrainingLoad returns both axes, and the systemic
ratio is worded as load progression, never injury risk. HRV is the weekly
average against the athlete's baseline band, never a single night. Sleep is
duration — last night and the 7-day average — read as an upper bound on a
fragmented night; stage minutes, body battery and stress are stored but returned
by no tool. TrainingReadiness returns as context for prescribing or revising a
Microcycle and never gates a single day. VO2max returns as a four-to-six-week
trend, expected to sit flat through strength-focused Mesocycles. Garmin's
monthly load-focus buckets return with their target bands.

**Strength progression is per Exercise, never combined.** The training summary
carries each Exercise's EstimatedOneRepMax from the best working set, labelled
an estimate, with the recorded top set and the rep scheme beside it, so a scheme
change reads as a discontinuity rather than a strength change; an estimate from
a set above ten repetitions is marked lower-confidence. Volume stays inside one
Activity's StrengthDetail and is never summed across Activities into a load.

**Tool surface.** This spec completes the eight-tool surface: read and update the
AthleteProfile from spec 02, plus `get_macrocycle` and `set_macrocycle`, read a training
summary for a date range, read current form, read ScheduledWorkouts for a date
range, and prescribe. Read tools are annotated read-only. The cap is a platform
constraint, not an aesthetic preference.

**Response sizing.** Summaries are aggregated server-side and returned as
structured content within the client's response budget. Returning raw Activity
rows for a model to aggregate would be slow, expensive in context, and arithmetically
unreliable.

## Testing Decisions

Good tests here describe coaching behaviour in the vocabulary of the domain. A
test named for prescribing a week under a Constraint is useful; a test named for a
repository method is not. Tests assert on tool responses, never on how the
prescription was computed internally.

**The .NET seam established in spec 02 is reused unchanged.** Activities,
ExerciseSets and wellness data are seeded through the ingest endpoint; Macrocycles,
Constraints and prescriptions are created through their own tools; assertions are
made on tool responses.

**Behaviours covered.** Storing a Macrocycle and reading back its ordered Goal
snapshot, Mesocycles and
WeekOutlines without loss of prose; resolving which Mesocycle and WeekOutline a
date falls in, including boundaries and dates outside the Macrocycle; prescribing
a Microcycle that matches its WeekOutline's allocation and recovery intent;
re-prescribing replacing a week rather than duplicating it;
prescribing under an availability Constraint producing fewer sessions;
prescribing under an injury Constraint substituting rather than deleting;
Constraint expiry ceasing to affect prescriptions; Adherence against
ScheduledWorkouts; Adherence against a WeekOutline's allocation where no
ScheduledWorkouts exist, including a lighter recovery week; and a training
summary aggregating correctly across a date range spanning Mesocycles. Tests
also cover a Macrocycle end date that disagrees with the shared Goal date and
verify that later AthleteProfile Goal edits leave the saved snapshot unchanged.

**Fixtures** derive from the athlete's real Macrocycle and from the captured
Garmin responses of spec 01, so that tests exercise realistic Mesocycle and
WeekOutline structures and real Activity shapes rather than invented ones.

**Prior art.** Spec 02 for the seam and the test host; spec 03 for seeding through
ingest.

## Out of Scope

Writing anything to Garmin, which is spec 05. Generating step-level workout
structures with targets and intervals, beyond what a ScheduledWorkout needs to
describe a session. Nutrition. Automatic Macrocycle revision without being asked.
Sharing a Macrocycle with a coach or training partner. Multi-athlete use beyond the
athlete identifier already in the schema.

## Further Notes

The [TrainingPeaks guide to macrocycles, mesocycles and microcycles](https://www.trainingpeaks.com/blog/macrocycles-mesocycles-and-microcycles-understanding-the-3-cycles-of-periodization/)
informs this hierarchy: long-range Goal planning, focused adaptation periods,
and weekly work and recovery. Its example lengths guide conversation, while
the athlete's Goal and schedule determine actual dates. WeekOutlines preserve
the intended build or recovery pattern before prescriptions exist.

The VO2max user story is the sharpest illustration of why this project is not a
Garmin MCP server. Answering it requires the Macrocycle, which Garmin does not hold;
the Activities, which it does; and ninety days of both in one queryable place,
which only the datastore provides. No existing tool can produce that answer, and
that gap is the entire justification for building rather than adopting.

The Macrocycle in hand runs October to March, so the first genuinely useful
prescription can be produced as soon as the Macrocycle is loaded and the sync has
recent history. That makes this spec the point at which the project starts paying
for itself.

The current-form response shapes and the two-axis TrainingLoad follow
[ADR 0009](../docs/adr/0009-trainingload-has-two-axes.md); the evidence behind
them is recorded in [the literature pass](literature-pass-2026-10-07.md).
