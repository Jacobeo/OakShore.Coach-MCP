# Spec 01 — Strength data spike

## Problem Statement

The thing that makes coach-mcp worth building rather than adopting an existing
Garmin MCP server is per-ExerciseSet detail — which Exercise, how many
repetitions, at what weight — combined with heart-rate zone distribution for
cardio Activities.

Every third-party Garmin tool the athlete has tried showed only that a strength
Activity happened, never the exercises, sets, reps or weights. Two explanations
fit that observation and they lead to completely different products:

1. The tools never called the exercise-sets endpoint, and the data has been there
   all along. The athlete creates Workouts in Garmin Connect, pushes them to the
   watch, and corrects reps and weight per set while lifting, which is the best
   case for Garmin's data model.
2. Garmin does not retain those corrections in a form the API exposes, in which
   case the strength analytics that justify the project cannot be built as
   imagined.

Research says the endpoint exists and returns `repetitionCount`, `weight` and an
exercise category and name, but also that watch-recorded strength data is often
inferred from the accelerometer with `weight` null and category `UNKNOWN`. Which
case applies to this athlete's own history is unknown, and unknowable without
looking.

Schema design, the strength analytics, the Adherence model and the entire
argument for building rather than adopting all rest on the answer.

## Solution

Before any schema exists, run a throwaway script from home against the real
Garmin account that fetches a handful of real Activities and dumps every response
to disk as raw, unparsed JSON. Then read them.

The output answers the question directly, and doubles as the fixture corpus that
both test seams will replay for the rest of the project's life.

## User Stories

1. As the athlete, I want to know whether my on-watch weight corrections survive into the Garmin API, so that I do not design a strength schema around data that does not exist.
2. As the athlete, I want to see a real `exerciseSets` payload from one of my own sessions, so that field names and units are settled by observation rather than by documentation.
3. As the athlete, I want to know whether `exercises[].name` contains a real exercise or `UNKNOWN`, so that I know whether exercise-level analysis is possible.
4. As the athlete, I want to know whether `weight` is populated and in which unit, so that the canonical unit at the ingest boundary can be chosen correctly.
5. As the athlete, I want to confirm that `setType` distinguishes working sets from rest, so that set counts are not inflated.
6. As the athlete, I want to see whether the Activity list really carries `totalSets`, `activeSets` and `totalReps`, so that the sync can avoid a per-Activity call where possible.
7. As the athlete, I want to see whether `summarizedExerciseSets` is populated in practice, so that per-Exercise aggregates can be trusted or discarded.
8. As the athlete, I want a real `hrTimeInZones` payload, so that time-in-zone analysis can be designed against its actual shape.
9. As the athlete, I want my configured heart-rate zone boundaries per sport profile, so that cross-sport comparison is not built on a single wrong set of zones.
10. As the athlete, I want to confirm that a first login from my home connection succeeds and writes a reusable token file, so that the sync can run headless afterwards.
11. As the athlete, I want the spike to stop immediately if Garmin rate-limits it, so that an exploratory script cannot get my IP blocked.
12. As the athlete, I want every raw response saved to disk, so that I never need to re-fetch the same data to answer a follow-up question.
13. As a developer, I want these dumps to become replay fixtures, so that later tests exercise real payload shapes rather than invented ones.
14. As the athlete, I want a strength Activity from a session where I followed a pushed Workout, and one from a freestyle session if any exist, so that the difference between the two cases is visible rather than assumed.
15. As the athlete, I want to inspect a run and a bike Activity, so that the cardio path is validated at the same time and not left as a second unknown.

## Implementation Decisions

**Runtime and placement.** Python, using `python-garminconnect` at 0.3.15 or
later. Run from the home machine over the residential connection, per ADR 0001.
Never from Azure, and never from a container on a cloud host.

**Authentication.** One interactive login, answering the MFA prompt by hand. The
resulting token file is persisted and reused; the spike must not log in again on
subsequent runs. This is the same token the sync in spec 03 will consume, so its
location is a decision, not an accident.

**Calls made.** A list call over a recent window of a few weeks; for each
returned Activity, the exercise-sets call when it is a strength Activity and the
heart-rate-zones call otherwise; and the zone-boundaries call once. The
per-Activity summary call is deliberately not used, because the list response
already carries the strength totals.

**Writes are forbidden.** The spike is read-only. In particular it must never
call the exercise-sets write endpoint, which has replace-all semantics over
recorded history, per ADR 0004.

**Pacing and abort.** At least a few seconds between calls. On 401, 403 or 429 the
script stops immediately and reports; it never retries, because retrying a rate
limit extends the block. Per ADR 0005.

**Output.** One file per call, raw response bytes, no parsing, no transformation,
no schema. Filenames identify the Activity and the endpoint. The point is to look
at what Garmin actually sent.

**Disposability.** This code is not the sync. It is not structured for reuse and
should not be grown into spec 03. Its durable output is the dumps, not the script.

## Testing Decisions

This is a spike, and it has no automated tests. Writing tests for throwaway code
that exists to answer a question would be ceremony.

Success is defined by the questions in the user stories being answerable from the
dumps. The spike is done when a person has read the output and can state, for
their own data, whether weight and exercise names are present.

The dumps themselves become the prior art for every later test: they are the
replay fixtures for the Python seam, and the source of the batch payloads used to
seed the .NET seam through the ingest endpoint.

## Out of Scope

Persisting anything to a datastore. Any .NET code. Any schema design. Parsing or
normalising the responses. Any Garmin write. Any handling of the bulk export
archive. Anything resembling a scheduled run.

## Further Notes

The two outcomes lead to different projects, and it is worth naming them now.

If weight and exercise names are present, the strength analytics are viable
immediately, the bulk export becomes valuable for backfilling years of it, and
Garmin write-back (spec 05) is a convenience that automates a manual step.

If they are absent, write-back is promoted from convenience to the mechanism that
makes the data exist at all, because the watch only records exercise names when
following a downloaded Workout — and spec 05 would move much earlier in the
order.

Either way the answer is worth an hour. Almost nothing else in this project has
that ratio.
