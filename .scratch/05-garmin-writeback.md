# Spec 05 — Garmin write-back

## Problem Statement

By spec 04 the coach prescribes a week and the ScheduledWorkouts live in the
datastore. They do not reach the watch.

The athlete's existing routine is to create a Workout in Garmin Connect by hand,
push it to the watch, and then correct repetitions and weight per set while
lifting. That manual step now sits awkwardly between a coach that knows exactly
what the session should be and a watch that does not. Every prescribed week means
re-entering, by hand, information the system already holds.

There is a second effect worth naming. Garmin's watch only records exercise names
reliably when it is following a downloaded Workout; freestyle strength sessions
come back with weight absent and the exercise category inferred from the
accelerometer. So the act of pushing a prescription to the watch is also what
makes the resulting Activity worth analysing. Prescription and instrumentation
are the same action.

Against that, writing to an unofficial API is a materially different risk from
reading one. A malformed write is visible in a real account, and one endpoint in
this area can destroy recorded history.

## Solution

Extend the sync, which already holds the Garmin session at home, to create
Workouts from ScheduledWorkouts, schedule them on the Garmin calendar and queue
them for delivery to the watch — within a deliberately narrow and reversible
write surface defined by ADR 0004.

Writes only ever add future intentions. Recorded history remains permanently
read-only.

## User Stories

1. As the athlete, I want my prescribed week pushed to Garmin, so that I stop re-entering sessions by hand.
2. As the athlete, I want pushed sessions to arrive on my watch, so that I can follow them at the gym.
3. As the athlete, I want strength sessions to carry the prescribed exercises, sets, repetitions and weights, so that the watch prompts me with the actual plan.
4. As the athlete, I want prescribed weights to reach the watch rather than being silently dropped, so that progressive overload is followed rather than remembered.
5. As the athlete, I want cardio sessions pushed with their duration and intensity, so that a prescribed easy ride is actually easy.
6. As the athlete, I want the session to appear on the correct date in Garmin, so that my calendar reflects the plan.
7. As the athlete, I want to confirm before anything is written to Garmin, so that a misunderstanding never reaches my account.
8. As the athlete, I want to remove a pushed session from Garmin, so that a changed week does not leave stale sessions behind.
9. As the athlete, I want re-prescribing a week to replace what was pushed, so that Garmin matches the current plan.
10. As the athlete, I want to see which prescribed sessions have been pushed and which have not, so that the two systems are never silently divergent.
11. As the athlete, I want an exercise the catalogue does not recognise to be reported before anything is sent, so that one bad name does not reject the whole session.
12. As the athlete, I want my recorded training history never modified, so that a bug in the write path cannot destroy the record.
13. As the athlete, I want a failed push reported to me, so that I do not arrive at the gym to an empty watch.
14. As the athlete, I want to push from my laptop even if the phone client cannot, so that a client limitation does not block the feature.
15. As the athlete, I want the sessions I then complete to come back enriched with exercise names, so that the loop closes and analysis improves.
16. As a developer, I want the write surface small and reversible, so that the blast radius of a bug is next week's calendar rather than years of history.

## Implementation Decisions

**Placement.** The write is performed by the home sync, not by the hosted server.
The sync already holds the authenticated Garmin session, runs from a residential
address, and is the only component permitted to contact Garmin at all (ADR 0001,
ADR 0003). The server records intent; the sync enacts it on its next run or when
triggered.

**Permitted operations, exhaustively.** Create a Workout; schedule a Workout to a
date; queue a Workout for delivery to a device; and the inverses of those. Nothing
else. In particular, scheduling in Garmin Connect alone does not reach the watch —
the device delivery step is separate and required, which is a common and
frustrating omission.

**Forbidden permanently.** Any write against a recorded Activity, and the
exercise-sets write endpoint specifically. It has replace-all semantics over real
training history and a partial or malformed payload destroys data that cannot be
recovered from Garmin. This is recorded in ADR 0004 so that a future contributor
who notices the gap understands it is deliberate.

**Validation before transmission.** Exercise identifiers are resolved against the
client's bundled exercise catalogue before any upload, because Garmin rejects an
entire payload atomically when one identifier is unrecognised. A validation
failure is reported against the specific prescribed session, not as a failed
batch.

**Confirmation.** Unlike prescription, which writes directly to our own store,
pushing to Garmin requires explicit confirmation. The asymmetry is deliberate:
prescribing is trivially reversible and touches only our data, whereas pushing
reaches a third-party account and a physical device. Write tools are not annotated
read-only, so clients will prompt — which here is a feature.

**State tracking.** Each ScheduledWorkout records whether it has been pushed and
what Garmin identifiers resulted, so that the systems can be reconciled, a push
can be undone, and re-prescription can clean up what it supersedes.

**Units.** Weight crosses three encodings between the exercise-sets JSON, FIT
files and workout steps. The canonical unit established at the ingest boundary in
spec 03 is converted once, at the edge, immediately before transmission.

**Client limitation, accepted.** Write tools taking arguments are reported to fail
silently in the ChatGPT mobile application, with the error hidden in the
interface. Pushing is therefore a desktop operation in practice. Reading
prescriptions on the phone, which is the common case, is unaffected. This is not
worked around.

## Testing Decisions

The write path is the highest-risk code in the project, so tests describe what is
sent and, equally importantly, what is never sent.

**The Python seam from spec 03 is reused.** The Garmin client is replayed, and the
sync under test is asserted on the requests it would issue. No automated test ever
contacts a live Garmin account.

**Behaviours covered.** A strength ScheduledWorkout producing a create request
carrying exercises, sets, repetitions and weights; weights surviving unit
conversion; a cardio ScheduledWorkout producing an appropriate create request; the
schedule request targeting the right date; the device delivery step being issued
and not merely the schedule; an unrecognised exercise identifier failing
validation before any request is made; removal issuing the correct inverse
operations; and re-prescription superseding previously pushed sessions.

**A negative test, asserted explicitly.** No code path under any input issues a
write against a recorded Activity or the exercise-sets endpoint. This encodes ADR
0004 as an executable constraint rather than a comment, so that a future change
violating it fails the build.

**The .NET side reuses the spec 02 seam** for the push-state transitions and the
confirmation-bearing tool, asserted through tool responses.

**Verified by hand once**, because it depends on a physical device: that a pushed
session actually appears on the watch, and that completing it produces an Activity
carrying exercise names — closing the loop this spec exists to create.

## Out of Scope

Editing or correcting recorded Activities in Garmin, permanently. Creating Garmin
TrainingPlans as opposed to individual Workouts. Two-way reconciliation of changes
made by hand in Garmin Connect. Uploading workout files directly, for which no
endpoint was found. Pushing from mobile clients.

## Further Notes

The weight of this spec depends on the outcome of spec 01. If the athlete's
existing strength Activities already carry exercise names and weights — which is
likely, since he follows pushed Workouts and corrects sets on the watch — then
this is a convenience that automates a manual step, and its position last in the
order is right.

If they do not, then pushing is the only mechanism that makes strength data exist,
the analytics in spec 04 are thin without it, and this spec should move ahead of
spec 04.

That is the one sequencing decision in the build order that the first hour of work
could still overturn, which is a good reason to do that hour first.
