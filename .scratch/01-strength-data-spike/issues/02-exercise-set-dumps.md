# 02: ExerciseSet dumps for strength Activities

**What to build:** For every strength Activity in the listed window, fetch the
exercise-sets response and dump it raw to disk. The athlete ends up with real
`exerciseSets` payloads from their own sessions, and can read field names and
units off the wire rather than off documentation.

Two cases matter and they must be distinguishable in the output: a session where
the athlete followed a Workout pushed to the watch, and a freestyle session if
any exist in the window. Research says watch-recorded strength data is often
inferred from the accelerometer with `weight` null and category `UNKNOWN`, while
a followed Workout is the best case for Garmin's data model. Whether that split
shows up in this athlete's history is the whole question, so make the difference
visible rather than assumed — if the window contains only one of the two cases,
say so explicitly instead of leaving it ambiguous.

Reuse the harness from ticket 01 as-is: same token, same pacing, same abort on
401/403/429.

Read-only, and specifically: the exercise-sets **write** endpoint is never
called. It has replace-all semantics over recorded history and a malformed
payload destroys training data that cannot be recovered from Garmin (ADR 0004).

The per-Activity summary call is deliberately not used — the list response from
ticket 01 already carries the strength totals.

**Blocked by:** 01 (Authenticated dump harness with Activity list)

**Status:** ready-for-agent

- [ ] Every strength Activity in the window has its exercise-sets response on
      disk as raw bytes
- [ ] At least one dumped Activity comes from a session that followed a pushed
      Workout
- [ ] A freestyle session is dumped if one exists in the window; if none does,
      that is recorded rather than left unstated
- [ ] Filenames tie each dump to its Activity and endpoint
- [ ] `set_activity_exercise_sets`, and every other write endpoint, is never
      called
- [ ] The per-Activity summary endpoint is not called
- [ ] Pacing and the no-retry abort from ticket 01 still hold across the whole
      per-Activity pass
