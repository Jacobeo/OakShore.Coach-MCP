# Spec 04 — Program and prescription

## Problem Statement

This is the use case that caused the project to exist, and nothing before it
delivers the actual value.

The athlete has a Goal with a date — a seven-a-side football season starting in
April — and a six-month Program produced in an earlier AI conversation: six
monthly Phases, each with a stated focus and a weekly allocation across cardio and
strength, shifting from an aerobic base through a strength emphasis to
match-specific conditioning and a taper.

That Program is a PDF. It cannot be queried, it cannot be compared against what
was actually done, and it is invisible to any tool. Meanwhile Garmin holds the
training history but has no concept of a multi-month periodized structure and no
way to export one, so even a perfect Garmin client could not answer the question
the athlete actually asks, which is: *given what I have done recently, my Program
and my current form, what should I do over the next week?*

Secondary questions follow the same shape: am I on track for my Goal, and where
should I change something to improve my VO2max?

Without this spec, the system is a well-built archive of training data. With it,
it is a coach.

## Solution

Model the Program and its Phases in the datastore as first-class objects, authored
and revised conversationally. Add the tools that let a model read the Program,
recent training and current form together, and write ScheduledWorkouts into the
store for the coming period.

Structure only what must be computed — dates, modalities, sessions per week — and
keep each Phase's stated focus as prose, because that intent is what lets the
agent substitute a session when a Constraint appears rather than merely deleting
it.

Adherence compares Activities against ScheduledWorkouts where they exist, and
against the Phase's weekly allocation where they do not, which covers historical
periods the coach never prescribed.

## User Stories

1. As the athlete, I want to paste my existing plan document and have it stored as a Program, so that six months of thinking is not trapped in a PDF.
2. As the athlete, I want to build a Program through conversation instead of a document, so that planning and recording are one activity.
3. As the athlete, I want a copy of my Program back in readable form whenever I ask, so that I am never locked into the tool.
4. As the athlete, I want each Phase's focus preserved in the words it was written in, so that the reasoning behind it is not flattened into numbers.
5. As the athlete, I want to be told which Phase today falls in, so that I know where I am in the six months.
6. As the athlete, I want to be told how far into the current Phase I am, so that I can judge whether to push or consolidate.
7. As the athlete, I want to ask what I should do this week and get specific sessions, so that I do not have to interpret the Program myself.
8. As the athlete, I want prescriptions to respect the current Phase's weekly allocation, so that the week serves the plan rather than my mood.
9. As the athlete, I want prescriptions to take my recent training load into account, so that a hard block is not stacked on top of fatigue.
10. As the athlete, I want prescriptions to account for how I have slept, so that a bad week is not treated as a normal one.
11. As the athlete, I want prescribed sessions written straight into the store when I ask for a week, so that there is no extra confirmation step.
12. As the athlete, I want to change my mind afterwards and have the week rewritten, so that a prescription is a starting point rather than a commitment.
13. As the athlete, I want to say I have picked up an injury and get a revised week, so that I keep training around the problem rather than stopping.
14. As the athlete, I want a revised week to preserve the Phase's intent by substituting sessions, so that a knock costs me a modality rather than the block.
15. As the athlete, I want to say I only have two evenings this week and get a shorter week that still prioritises correctly, so that reduced availability does not mean an abandoned plan.
16. As the athlete, I want a temporary Constraint to keep applying to next week's prescription until it expires, so that I do not have to repeat myself.
17. As the athlete, I want to ask what I am doing today and get a short answer, so that it is usable on a phone in the morning.
18. As the athlete, I want to ask what I am doing this week and see the prescribed sessions, so that I can plan around them.
19. As the athlete, I want to know how much of the last month I actually completed, so that I can see drift before it becomes a pattern.
20. As the athlete, I want to know whether I am on track for my Goal, so that I can correct early rather than discover the gap in April.
21. As the athlete, I want Adherence measured against specific prescribed sessions where they exist, so that the assessment is precise rather than a count.
22. As the athlete, I want Adherence measured against the Phase's weekly allocation for periods that were never prescribed, so that my existing history is still assessable.
23. As the athlete, I want to see how my training splits across intensity, so that I can tell whether I am training the middle at both ends.
24. As the athlete, I want to know where to change something to improve my VO2max, so that I get a specific action rather than general advice.
25. As the athlete, I want my strength progression over months, so that I can see whether loads are actually increasing.
26. As the athlete, I want to compare a period against an earlier one, so that I can tell whether this winter is better than the last.
27. As the athlete, I want the coach to say when its data is stale, so that I can discount its confidence accordingly.
28. As the athlete, I want the coach to say when it lacks the data to answer, so that I am not given a confident guess.
29. As a developer, I want the tool count kept small, so that model tool selection does not degrade.
30. As a developer, I want the server never to call a language model, so that it stays deterministic, testable and free to run.

## Implementation Decisions

**Program and Phase are ours, not Garmin's.** Garmin has no equivalent concept and
none is importable. A Program carries a Goal, a date range and an ordered series
of Phases. A Phase carries a date range, a focus expressed as prose, and a weekly
allocation by modality. This separation from Garmin's TrainingPlan is deliberate
and is recorded in CONTEXT.md: a TrainingPlan is a series of dated,
step-level workouts, whereas a Program describes intent and proportion.

**Structure only what must be computed.** Dates, modalities and sessions per week
become columns because Adherence counts them. The focus text and the surrounding
reasoning stay as prose, because that is what allows an intelligent substitution
when a Constraint applies. Over-structuring the prose into scalar intensity levels
would destroy the capability that makes the injury conversation work.

**The model parses; the server stores.** Converting a pasted plan document into
Phases is done by the client model, which is good at exactly that, and the
structured result is submitted through a tool. The server never calls a language
model. This keeps it deterministic, cheap, offline-testable, and free of
inference costs and API keys.

**Prescription writes directly.** A request to prescribe a period writes
ScheduledWorkouts into the store immediately rather than proposing a draft for
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
modality against the Phase's weekly allocation. The first is precise and comes
free because the coach itself wrote the prescriptions; the second covers history
that predates the coach.

**Intensity analysis is deferred but anticipated.** The initial Adherence measure
is completion, not intensity. Zone-based analysis — whether prescribed easy
sessions were actually easy — is the more valuable answer and depends on the
zone data captured in spec 03. It is built when real gaps in the simpler answer
justify it, and the schema does not preclude it.

**Tool surface.** This spec completes the eight-tool surface: read and update the
AthleteProfile from spec 02, plus read and set the Program, read a training
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
ExerciseSets and wellness data are seeded through the ingest endpoint; Programs,
Constraints and prescriptions are created through their own tools; assertions are
made on tool responses.

**Behaviours covered.** Storing a Program and reading it back without loss of its
prose; resolving which Phase a date falls in, including boundaries and dates
outside the Program; prescribing a week that matches the current Phase's
allocation; re-prescribing replacing a period rather than duplicating it;
prescribing under an availability Constraint producing fewer sessions;
prescribing under an injury Constraint substituting rather than deleting;
Constraint expiry ceasing to affect prescriptions; Adherence against
ScheduledWorkouts; Adherence against a Phase allocation where no ScheduledWorkouts
exist; and a training summary aggregating correctly across a date range spanning
more than one Phase.

**Fixtures** derive from the athlete's real Program and from the captured Garmin
responses of spec 01, so that tests exercise realistic Phase structures and real
Activity shapes rather than invented ones.

**Prior art.** Spec 02 for the seam and the test host; spec 03 for seeding through
ingest.

## Out of Scope

Writing anything to Garmin, which is spec 05. Generating step-level workout
structures with targets and intervals, beyond what a ScheduledWorkout needs to
describe a session. Nutrition. Automatic Program revision without being asked.
Sharing a Program with a coach or training partner. Multi-athlete use beyond the
athlete identifier already in the schema.

## Further Notes

The VO2max user story is the sharpest illustration of why this project is not a
Garmin MCP server. Answering it requires the Program, which Garmin does not hold;
the Activities, which it does; and ninety days of both in one queryable place,
which only the datastore provides. No existing tool can produce that answer, and
that gap is the entire justification for building rather than adopting.

The Program in hand runs October to March, so the first genuinely useful
prescription can be produced as soon as the Program is loaded and the sync has
recent history. That makes this spec the point at which the project starts paying
for itself.
